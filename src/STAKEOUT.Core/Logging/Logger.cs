namespace Stakeout.Core;

/// <summary>
/// Application-wide action log (static facade over <see cref="EncryptedLogFile"/>).
/// Every tweak, download and removal is recorded, encrypted with AES-256-GCM, in
/// %TEMP%\STAKEOUT\stakeout-yyyyMMdd.log. Before <see cref="Init"/> all calls are
/// no-ops, and logging failures never propagate to callers.
/// </summary>
public static class Logger
{
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

    public static void LogError(string action, Exception ex) => Log(action, "ERROR", ex.Message);

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
