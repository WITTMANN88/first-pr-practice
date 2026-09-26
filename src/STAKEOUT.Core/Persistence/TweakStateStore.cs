using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>On-disk shape of tweak-state.json (versioned for future migrations).</summary>
public sealed class TweakStateDocument
{
    public int Version { get; set; } = TweakStateStore.CurrentVersion;
    public IDictionary<string, TweakState> Tweaks { get; init; } = new Dictionary<string, TweakState>();
}

/// <summary>
/// Compile-time generated (reflection-free) JSON serializer for the state file.
/// Reflection-based System.Text.Json is trim-unsafe: in a trimmed build the
/// document's properties could be removed and the file written silently empty.
/// Source generation also avoids reflection cost at startup.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TweakStateDocument))]
internal sealed partial class TweakStateJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Persists which tweaks are applied and the original registry values captured at
/// apply time — the data "Отменить всё" (revert all) depends on, including after
/// the app has been closed and reopened.
///
/// Guarantees:
///   * Thread-safe: every read/write of the in-memory map happens under one lock,
///     and only deep copies go in or out.
///   * Atomic save: JSON goes to a temp file which then replaces the target.
///   * Rotating backups: after every successful save a copy is written to
///     "backups/" and only the newest <see cref="BackupsToKeep"/> (3) are kept.
///     Backups follow the current state (apply AND revert); backing up only on
///     apply would let a later recovery resurrect already-reverted tweaks.
///   * Self-healing load: a corrupt file is quarantined as "*.corrupt-{timestamp}"
///     (kept for manual inspection); a corrupt or missing file is then restored
///     from the newest backup that parses, and the main file is rewritten.
/// </summary>
public sealed class TweakStateStore
{
    public const int CurrentVersion = 1;
    public const int DefaultBackupsToKeep = 3;

    private const string BackupPrefix = "tweak-state.";
    private const string BackupSearchPattern = "tweak-state.*Z.json";
    private const string BackupStampFormat = "yyyyMMdd'T'HHmmss'.'fffffff";

    /// <summary>Machine-wide default location (most tweaks live in HKLM).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "STAKEOUT", "tweak-state.json");

    private readonly object _gate = new();
    private readonly Dictionary<string, TweakState> _state;
    private readonly bool _persist;
    private readonly Func<DateTime> _utcNow;
    private long _lastBackupTicks;

    public TweakStateStore(string filePath, int backupsToKeep = DefaultBackupsToKeep)
        : this(filePath, backupsToKeep, () => DateTime.UtcNow) { }

    /// <summary>Test hook: inject the clock used to stamp backups.</summary>
    internal TweakStateStore(string filePath, int backupsToKeep, Func<DateTime> utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(backupsToKeep);
        FilePath = filePath;
        BackupsToKeep = backupsToKeep;
        BackupDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".", "backups");
        _persist = true;
        _utcNow = utcNow;
        _lastBackupTicks = NewestBackupTicks();

        _state = Load(out var recoveredFrom);
        if (recoveredFrom != null)
        {
            RecoveredFrom = recoveredFrom;
            // Make the main file valid again. No new backup: it would be a
            // duplicate of the one we just restored and would evict an older one.
            lock (_gate) SaveLocked(backup: false);
        }
    }

    private TweakStateStore()
    {
        FilePath = BackupDirectory = string.Empty;
        _persist = false;
        _utcNow = () => DateTime.UtcNow;
        _state = new();
    }

    /// <summary>A store that never touches the disk (design-time data, previews).</summary>
    public static TweakStateStore CreateInMemory() => new();

    public string FilePath { get; }
    public string BackupDirectory { get; }
    public int BackupsToKeep { get; }

    /// <summary>Backup file the state was restored from at startup, or null.</summary>
    public string? RecoveredFrom { get; }

    public bool IsApplied(string tweakId)
    {
        lock (_gate) return _state.TryGetValue(tweakId, out var s) && s.Applied;
    }

    /// <summary>Deep copy of an applied tweak's rollback record, or null.</summary>
    public TweakState? GetApplied(string tweakId)
    {
        lock (_gate)
            return _state.TryGetValue(tweakId, out var s) && s.Applied ? s.Clone() : null;
    }

    public IReadOnlyList<string> AppliedTweakIds()
    {
        lock (_gate) return _state.Where(kv => kv.Value.Applied).Select(kv => kv.Key).ToList();
    }

    /// <summary>Record a tweak as applied with its captures. Returns whether it was persisted.</summary>
    public bool MarkApplied(string tweakId, TweakState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var copy = state.Clone();
        copy.Applied = true;
        copy.AppliedUtc = _utcNow();
        lock (_gate)
        {
            _state[tweakId] = copy;
            return SaveLocked(backup: true);
        }
    }

    /// <summary>Forget a tweak after a successful revert. Returns whether it was persisted.</summary>
    public bool MarkReverted(string tweakId)
    {
        lock (_gate)
        {
            _state.Remove(tweakId);
            return SaveLocked(backup: true);
        }
    }

    /// <summary>Backup files, newest first.</summary>
    public IReadOnlyList<string> GetBackups()
    {
        if (!_persist || !Directory.Exists(BackupDirectory)) return Array.Empty<string>();
        return Directory.GetFiles(BackupDirectory, BackupSearchPattern)
            .Where(f => TryParseStamp(f, out _))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();
    }

    // --- load / recovery ---------------------------------------------------

    private Dictionary<string, TweakState> Load(out string? recoveredFrom)
    {
        recoveredFrom = null;
        try
        {
            if (File.Exists(FilePath))
                return Parse(File.ReadAllText(FilePath), FilePath);

            if (GetBackups().Count == 0) return new(); // genuine first run
            Logger.Log("TweakStateStore", "WARNING", "state file missing; trying backups");
        }
        catch (JsonException ex)
        {
            Quarantine(ex);
        }
        catch (Exception ex)
        {
            // I/O or permission problem: start empty, leave the file untouched.
            Logger.LogError("TweakStateStore.Load", ex);
            return new();
        }

        foreach (var backup in GetBackups())
        {
            try
            {
                var restored = Parse(File.ReadAllText(backup), backup);
                recoveredFrom = backup;
                Logger.Log("TweakStateStore", "RECOVERED", $"state restored from {Path.GetFileName(backup)}");
                return restored;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Logger.Log("TweakStateStore", "WARNING", $"skipping unusable backup {Path.GetFileName(backup)}: {ex.Message}");
            }
        }
        return new();
    }

    private static Dictionary<string, TweakState> Parse(string json, string source)
    {
        var doc = JsonSerializer.Deserialize(json, TweakStateJsonContext.Default.TweakStateDocument)
                  ?? throw new JsonException($"{source}: document is null.");
        if (doc.Version > CurrentVersion)
        {
            Logger.Log("TweakStateStore", "WARNING",
                $"{Path.GetFileName(source)} has version {doc.Version} (> {CurrentVersion}); reading best-effort");
        }
        return doc.Tweaks is null ? new() : new Dictionary<string, TweakState>(doc.Tweaks);
    }

    private void Quarantine(Exception reason)
    {
        try
        {
            var target = $"{FilePath}.corrupt-{_utcNow():yyyyMMddHHmmssfff}";
            File.Move(FilePath, target);
            Logger.Log("TweakStateStore", "ERROR", $"corrupt state file moved to {target}: {reason.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError("TweakStateStore.Quarantine", ex);
        }
    }

    // --- save / backups ----------------------------------------------------

    /// <summary>Serialize and atomically replace the file; then back up. Caller holds the lock.</summary>
    private bool SaveLocked(bool backup)
    {
        if (!_persist) return true;

        string json;
        try
        {
            var doc = new TweakStateDocument { Version = CurrentVersion, Tweaks = _state };
            json = JsonSerializer.Serialize(doc, TweakStateJsonContext.Default.TweakStateDocument);
            WriteAtomic(FilePath, json);
        }
        catch (Exception ex)
        {
            Logger.LogError("TweakStateStore.Save", ex);
            return false; // no backup of a state that was not persisted
        }

        // A backup problem must never fail the save itself.
        if (backup && BackupsToKeep > 0)
        {
            try
            {
                WriteBackupLocked(json);
            }
            catch (Exception ex)
            {
                Logger.LogError("TweakStateStore.Backup", ex);
            }
        }
        return true;
    }

    private void WriteBackupLocked(string json)
    {
        Directory.CreateDirectory(BackupDirectory);

        // Strictly increasing stamps, even if the clock moves backwards, so name
        // order is always save order and pruning never removes the newest copy.
        var ticks = Math.Max(_utcNow().ToUniversalTime().Ticks, _lastBackupTicks + 1);
        _lastBackupTicks = ticks;
        var stamp = new DateTime(ticks, DateTimeKind.Utc).ToString(BackupStampFormat, CultureInfo.InvariantCulture);
        WriteAtomic(Path.Combine(BackupDirectory, $"{BackupPrefix}{stamp}Z.json"), json);

        foreach (var old in GetBackups().Skip(BackupsToKeep))
        {
            try { File.Delete(old); }
            catch (Exception ex) { Logger.LogError("TweakStateStore.Prune", ex); }
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    private long NewestBackupTicks()
    {
        var backups = GetBackups();
        return backups.Count > 0 && TryParseStamp(backups[0], out var ticks) ? ticks : 0;
    }

    /// <summary>Parse "tweak-state.{stamp}Z.json"; rejects files that merely match the glob.</summary>
    private static bool TryParseStamp(string path, out long ticks)
    {
        ticks = 0;
        var name = Path.GetFileName(path);
        if (!name.StartsWith(BackupPrefix, StringComparison.Ordinal) ||
            !name.EndsWith("Z.json", StringComparison.Ordinal))
        {
            return false;
        }

        var stamp = name[BackupPrefix.Length..^"Z.json".Length];
        if (!DateTime.TryParseExact(stamp, BackupStampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return false;
        }
        ticks = dt.Ticks;
        return true;
    }
}
