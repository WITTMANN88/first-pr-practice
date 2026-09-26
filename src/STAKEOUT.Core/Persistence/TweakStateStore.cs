using System.Text.Json;
using System.Text.Json.Serialization;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>On-disk shape of tweak-state.json (versioned for future migrations).</summary>
public sealed class TweakStateDocument
{
    public int Version { get; set; } = TweakStateStore.CurrentVersion;
    public Dictionary<string, TweakState> Tweaks { get; set; } = new();
}

/// <summary>
/// Persists which tweaks are applied and the original registry values captured at
/// apply time — the data "Отменить всё" (revert all) depends on, including after
/// the app has been closed and reopened.
///
/// Guarantees:
///   * Thread-safe: every read/write of the in-memory map happens under one lock,
///     and only deep copies go in or out, so callers never share mutable state
///     with a concurrent save.
///   * Atomic save: JSON is written to a temp file, then moved over the target,
///     so a crash mid-write cannot leave a truncated file behind.
///   * Corruption-tolerant load: an unreadable file is quarantined as
///     "*.corrupt-{timestamp}" (kept for manual recovery) and the store starts empty
///     instead of crashing the app.
/// </summary>
public sealed class TweakStateStore
{
    public const int CurrentVersion = 1;

    /// <summary>Machine-wide default location (most tweaks live in HKLM).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "STAKEOUT", "tweak-state.json");

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, TweakState> _state;

    public TweakStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        _state = Load();
    }

    public string FilePath { get; }

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
        copy.AppliedUtc = DateTime.UtcNow;
        lock (_gate)
        {
            _state[tweakId] = copy;
            return SaveLocked();
        }
    }

    /// <summary>Forget a tweak after a successful revert. Returns whether it was persisted.</summary>
    public bool MarkReverted(string tweakId)
    {
        lock (_gate)
        {
            _state.Remove(tweakId);
            return SaveLocked();
        }
    }

    // --- persistence --------------------------------------------------------

    private Dictionary<string, TweakState> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();

            var json = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize<TweakStateDocument>(json, JsonOptions)
                      ?? throw new JsonException("Document is null.");

            if (doc.Version > CurrentVersion)
                Logger.Log("TweakStateStore", "WARNING",
                    $"state file version {doc.Version} is newer than supported {CurrentVersion}; reading best-effort");

            return new Dictionary<string, TweakState>(doc.Tweaks ?? new());
        }
        catch (JsonException ex)
        {
            Quarantine(ex);
            return new();
        }
        catch (Exception ex)
        {
            // I/O or permission problem: start empty but leave the file untouched.
            Logger.LogError("TweakStateStore.Load", ex);
            return new();
        }
    }

    private void Quarantine(Exception reason)
    {
        try
        {
            var target = $"{FilePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(FilePath, target);
            Logger.Log("TweakStateStore", "ERROR", $"corrupt state file moved to {target}: {reason.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError("TweakStateStore.Quarantine", ex);
        }
    }

    /// <summary>Serialize and atomically replace the file. Caller holds the lock.</summary>
    private bool SaveLocked()
    {
        var tmp = FilePath + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var doc = new TweakStateDocument { Version = CurrentVersion, Tweaks = _state };
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("TweakStateStore.Save", ex);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            return false;
        }
    }
}
