namespace Stakeout.Core;

/// <summary>
/// Application-wide action log (static facade over <see cref="EncryptedLogFile"/>).
/// Every tweak, download and removal is recorded, encrypted with AES-256-GCM, in
/// %TEMP%\STAKEOUT\stakeout-yyyyMMdd.log. Before <see cref="Init"/> all calls are
/// no-ops, and logging failures never propagate to callers.
/// </summary>
public static class Logger
{
    /// <summary>Exception chain levels <see cref="Describe"/> prints; real chains are a few deep.</summary>
    private const int MaxChainDepth = 8;

    private static readonly object Gate = new();
    private static EncryptedLogFile? _file;

    /// <summary>Absolute path of the active log file ("" before Init).</summary>
    public static string LogPath => _file?.Path ?? string.Empty;

    public static bool IsInitialized => _file != null;

    /// <summary>
    /// Create (or reuse) today's log file and write a session header. Safe to call
    /// more than once; only the first call takes effect.
    /// </summary>
    /// <param name="directory">Log folder; defaults to %TEMP%\STAKEOUT.</param>
    /// <param name="machineName">Key binding; defaults to the current machine.</param>
    public static void Init(string? directory = null, string? machineName = null)
    {
        lock (Gate)
        {
            if (_file != null) return;
            try
            {
                var dir = directory ?? Path.Combine(Path.GetTempPath(), "STAKEOUT");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"stakeout-{DateTime.Now:yyyyMMdd}.log");
                _file = new EncryptedLogFile(path, LogCipher.ForMachine(machineName ?? Environment.MachineName));
            }
            catch
            {
                // TEMP unavailable: degrade to a no-op logger rather than crash.
                _file = null;
                return;
            }
        }

        Log("Logger", "OK", $"Session started ({Environment.OSVersion.VersionString})");
    }

    /// <summary>Record an action with its status and an optional detail string.</summary>
    public static void Log(string action, string status, string? detail = null)
    {
        var file = _file;
        if (file == null) return;
        try
        {
            file.Append(EncryptedLogFile.FormatLine(DateTime.Now, action, status, detail));
        }
        catch
        {
            // Never let logging failures reach the UI.
        }
    }

    /// <summary>
    /// Record a handled failure as one readable line: the type and message of
    /// every exception in the chain (no stack trace).
    /// </summary>
    public static void LogError(string action, Exception ex) => Log(action, "ERROR", Describe(ex));

    /// <summary>
    /// Record a failure the app cannot recover from (startup, unhandled
    /// exceptions) in full: types, messages and stack traces of the whole chain.
    /// </summary>
    public static void LogFatal(string action, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Log(action, "FATAL", ex.ToString());
    }

    /// <summary>
    /// "Outer.Type: message → Inner.Type: message", outermost first. Every level
    /// names its type, so the text is never blank even for exceptions thrown
    /// without a message.
    /// </summary>
    public static string Describe(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var parts = new List<string>();
        for (var e = ex; e != null && parts.Count < MaxChainDepth; e = e.InnerException)
        {
            var type = e.GetType().FullName ?? e.GetType().Name;
            var message = e.Message.Trim();
            parts.Add(message.Length == 0 ? type : $"{type}: {message}");
        }
        return string.Join(" → ", parts);
    }

    /// <summary>Decrypt the current log back to plaintext lines (bad lines skipped).</summary>
    public static IEnumerable<string> ReadDecrypted()
    {
        var file = _file;
        if (file == null) return Array.Empty<string>();
        try
        {
            return file.ReadAll();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
