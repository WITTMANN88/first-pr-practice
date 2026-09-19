using System.Text.Json;
using System.Text.Json.Serialization;

namespace Observation.Core.Journal;

/// <summary>
/// Построчный JSON-лог (выбран из двух вариантов в плане — "SQLite или построчный
/// JSON-лог" — как более простой и полностью портативный: удалил Observation_Data,
/// ничего не осталось, без файла БД). Журнал (заголовки пакетов + записи твиков) —
/// append-only; "активные твики" — отдельный файл, перезаписывается целиком при
/// каждом изменении (это текущий срез, не история — см. план).
/// </summary>
public sealed class JsonLinesJournalStore : IJournalStore
{
    private const int MaxEntries = 2000;
    private const long MaxSizeBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _journalPath;
    private readonly string _archiveFolder;
    private readonly string _activeTweaksPath;
    private readonly object _lock = new();

    public JsonLinesJournalStore(string dataFolderPath)
    {
        Directory.CreateDirectory(dataFolderPath);
        _journalPath = Path.Combine(dataFolderPath, "journal.jsonl");
        _archiveFolder = Path.Combine(dataFolderPath, "journal_archive");
        _activeTweaksPath = Path.Combine(dataFolderPath, "active_tweaks.json");
    }

    public void BeginBatch(Guid batchId, int tweaksPlanned)
    {
        lock (_lock)
        {
            AppendLine(new JournalLine("batch", batchId, DateTimeOffset.UtcNow, BatchStatus.InProgress, tweaksPlanned,
                null, null, null, null, null, null));
        }
    }

    public void AppendEntry(JournalEntry entry)
    {
        lock (_lock)
        {
            AppendLine(new JournalLine("entry", entry.BatchId, entry.Timestamp, null, null,
                entry.TweakId, entry.Success, entry.Message, entry.PreviousValueJson, entry.NewValueJson, entry.UserSid));
        }
    }

    public void CompleteBatch(Guid batchId) => FinishBatch(batchId, BatchStatus.Completed);

    public void AcknowledgeIncomplete(Guid batchId) => FinishBatch(batchId, BatchStatus.AcknowledgedIncomplete);

    public BatchHeader? FindIncompleteBatch()
    {
        lock (_lock)
        {
            var lines = ReadAllLines();
            foreach (var group in lines.Where(l => l.Kind == "batch").GroupBy(l => l.BatchId))
            {
                var ordered = group.OrderBy(l => l.Timestamp).ToList();
                if (ordered[^1].Status != BatchStatus.InProgress)
                    continue;

                var first = ordered[0];
                var applied = lines.Count(l => l.Kind == "entry" && l.BatchId == group.Key);
                return new BatchHeader
                {
                    BatchId = group.Key,
                    StartedAt = first.Timestamp,
                    TweaksPlanned = first.TweaksPlanned ?? 0,
                    Status = BatchStatus.InProgress,
                    TweaksApplied = applied
                };
            }

            return null;
        }
    }

    public IReadOnlyList<JournalEntry> GetEntriesForBatch(Guid batchId)
    {
        lock (_lock)
        {
            return ReadAllLines()
                .Where(l => l.Kind == "entry" && l.BatchId == batchId)
                .Select(ToJournalEntry)
                .ToList();
        }
    }

    public JournalEntry? GetLatestEntryForTweak(string tweakId)
    {
        lock (_lock)
        {
            return ReadAllLines()
                .Where(l => l.Kind == "entry" && l.TweakId == tweakId && l.Success == true)
                .OrderByDescending(l => l.Timestamp)
                .Select(ToJournalEntry)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<JournalEntry> GetRecentEntries(int count)
    {
        lock (_lock)
        {
            return ReadAllLines()
                .Where(l => l.Kind == "entry")
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .Select(ToJournalEntry)
                .ToList();
        }
    }

    public IReadOnlyList<ActiveTweakState> GetActiveTweaks()
    {
        lock (_lock)
        {
            return ReadActiveTweaksFile().Values.ToList();
        }
    }

    public void UpsertActiveTweak(ActiveTweakState state)
    {
        lock (_lock)
        {
            var all = ReadActiveTweaksFile();
            all[Key(state.TweakId, state.UserSid)] = state;
            WriteActiveTweaksFile(all);
        }
    }

    public void RemoveActiveTweak(string tweakId, string? userSid)
    {
        lock (_lock)
        {
            var all = ReadActiveTweaksFile();
            all.Remove(Key(tweakId, userSid));
            WriteActiveTweaksFile(all);
        }
    }

    private void FinishBatch(Guid batchId, BatchStatus status)
    {
        lock (_lock)
        {
            AppendLine(new JournalLine("batch", batchId, DateTimeOffset.UtcNow, status, null,
                null, null, null, null, null, null));
            RotateIfNeeded();
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_journalPath))
            return;

        var info = new FileInfo(_journalPath);
        var lineCount = File.ReadLines(_journalPath).Count();
        if (lineCount <= MaxEntries && info.Length <= MaxSizeBytes)
            return;

        Directory.CreateDirectory(_archiveFolder);
        var archivePath = Path.Combine(_archiveFolder, $"journal_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json");
        File.Move(_journalPath, archivePath);
    }

    private void AppendLine(JournalLine line) =>
        File.AppendAllText(_journalPath, JsonSerializer.Serialize(line, JsonOptions) + Environment.NewLine);

    private List<JournalLine> ReadAllLines()
    {
        if (!File.Exists(_journalPath))
            return new List<JournalLine>();

        return File.ReadLines(_journalPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<JournalLine>(l, JsonOptions)!)
            .ToList();
    }

    private static JournalEntry ToJournalEntry(JournalLine l) => new()
    {
        BatchId = l.BatchId,
        TweakId = l.TweakId!,
        Timestamp = l.Timestamp,
        Success = l.Success ?? false,
        Message = l.Message,
        PreviousValueJson = l.PreviousValueJson,
        NewValueJson = l.NewValueJson,
        UserSid = l.UserSid
    };

    private static string Key(string tweakId, string? userSid) => $"{tweakId}|{userSid ?? ""}";

    private Dictionary<string, ActiveTweakState> ReadActiveTweaksFile()
    {
        if (!File.Exists(_activeTweaksPath))
            return new Dictionary<string, ActiveTweakState>();

        var json = File.ReadAllText(_activeTweaksPath);
        return JsonSerializer.Deserialize<Dictionary<string, ActiveTweakState>>(json, JsonOptions) ?? new();
    }

    private void WriteActiveTweaksFile(Dictionary<string, ActiveTweakState> all) =>
        File.WriteAllText(_activeTweaksPath, JsonSerializer.Serialize(all, JsonOptions));

    private sealed record JournalLine(
        string Kind,
        Guid BatchId,
        DateTimeOffset Timestamp,
        BatchStatus? Status,
        int? TweaksPlanned,
        string? TweakId,
        bool? Success,
        string? Message,
        string? PreviousValueJson,
        string? NewValueJson,
        string? UserSid);
}
