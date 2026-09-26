using System.Globalization;
using Stakeout.Core;
using Stakeout.Services;

namespace Stakeout.Tests.TestSupport;

/// <summary>Unique temporary directory, deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stakeout-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Sets the current thread's (UI) culture for the duration of a test. Strings
/// resolves text from CurrentUICulture, so this isolates tests from each other
/// and from the machine's locale without touching process-wide state.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    public CultureScope(string name)
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        var c = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = c;
        CultureInfo.CurrentUICulture = c;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}

/// <summary>
/// In-memory <see cref="IRegistryAccess"/>. Keys are case-insensitive like the
/// real registry; stored arrays are copied so callers cannot alias them. Writes
/// can be made to fail selectively to exercise rollback-on-failure paths.
/// </summary>
public sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<string, (object Value, RegValueKind Kind)> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Return true to make a SetValue/DeleteValue call fail.</summary>
    public Func<string, string, bool>? FailWhen { get; set; }

    public int WriteCount { get; private set; }

    private static string Key(RegHive hive, string subKey, string name) => $"{hive}\\{subKey}\\{name}";

    public void Seed(RegHive hive, string subKey, string name, object value, RegValueKind kind)
        => _values[Key(hive, subKey, name)] = (Copy(value), kind);

    public bool Contains(RegHive hive, string subKey, string name) => _values.ContainsKey(Key(hive, subKey, name));

    public (object Value, RegValueKind Kind) Get(RegHive hive, string subKey, string name)
        => _values[Key(hive, subKey, name)];

    public RegistryValueSnapshot Capture(RegHive hive, string subKey, string name)
        => _values.TryGetValue(Key(hive, subKey, name), out var v)
            ? new RegistryValueSnapshot(Copy(v.Value), v.Kind, true)
            : RegistryValueSnapshot.Absent;

    public bool SetValue(RegHive hive, string subKey, string name, object value, RegValueKind kind)
    {
        if (FailWhen?.Invoke(subKey, name) == true) return false;
        WriteCount++;
        _values[Key(hive, subKey, name)] = (Copy(value), kind);
        return true;
    }

    public bool DeleteValue(RegHive hive, string subKey, string name)
    {
        if (FailWhen?.Invoke(subKey, name) == true) return false;
        _values.Remove(Key(hive, subKey, name));
        return true;
    }

    /// <summary>Deep snapshot of all values, for before/after comparisons.</summary>
    public Dictionary<string, (object Value, RegValueKind Kind)> Snapshot()
        => _values.ToDictionary(kv => kv.Key, kv => (Copy(kv.Value.Value), kv.Value.Kind), StringComparer.OrdinalIgnoreCase);

    public static bool ValueEquals(object a, object b) => (a, b) switch
    {
        (byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y),
        (string[] x, string[] y) => x.SequenceEqual(y),
        _ => Equals(a, b),
    };

    private static object Copy(object v) => v switch
    {
        byte[] b => (byte[])b.Clone(),
        string[] s => (string[])s.Clone(),
        _ => v,
    };
}

/// <summary>Runs posted actions immediately (single-threaded test "UI thread").</summary>
public sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>Queues posted actions until <see cref="RunAll"/> — models a busy UI thread.</summary>
public sealed class QueueDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();
    private readonly object _gate = new();

    public int Pending { get { lock (_gate) return _queue.Count; } }

    public void Post(Action action) { lock (_gate) _queue.Enqueue(action); }

    public void RunAll()
    {
        while (true)
        {
            Action next;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                next = _queue.Dequeue();
            }
            next();
        }
    }
}

/// <summary>Delay function whose completions the test controls explicitly.</summary>
public sealed class ManualDelay
{
    private readonly List<TaskCompletionSource> _pending = new();

    public int PendingCount { get { lock (_pending) return _pending.Count; } }

    public Task Delay(TimeSpan _, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        lock (_pending) _pending.Add(tcs);
        return tcs.Task;
    }

    /// <summary>Complete every pending delay (i.e. "the lifetime has elapsed").</summary>
    public void ElapseAll()
    {
        List<TaskCompletionSource> due;
        lock (_pending) { due = _pending.ToList(); _pending.Clear(); }
        foreach (var t in due) t.TrySetResult();
    }
}

public static class Wait
{
    /// <summary>Poll until <paramref name="condition"/> holds (for async continuations), or fail.</summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(5);
        }
    }
}
