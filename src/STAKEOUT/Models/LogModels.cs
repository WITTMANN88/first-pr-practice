using System.Text.RegularExpressions;

namespace Stakeout.Models;

/// <summary>Severity used to colour a log line's status in the viewer.</summary>
public enum LogLevel { Info, Success, Error }

/// <summary>
/// One decrypted log line split into its parts.
/// Source format (see Logger): "[HH:mm:ss] [Action] [Status] detail".
/// Lines that do not match are kept verbatim in <see cref="Detail"/>.
/// </summary>
public sealed partial class LogEntry
{
    public string Time { get; init; } = "";
    public string Action { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
    public LogLevel Level { get; init; } = LogLevel.Info;

    // Action is matched lazily up to "] [" so an action containing ']' elsewhere
    // does not swallow the status field.
    [GeneratedRegex(@"^\[(?<t>[^\]]*)\] \[(?<a>.*?)\] \[(?<s>[^\]]*)\]\s?(?<d>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    private static readonly string[] ErrorStatuses =
        { "ERROR", "ACCESS_DENIED", "TIMEOUT", "FAILED", "PARTIAL", "REVERT-PARTIAL", "BLOCKED", "CANCELLED" };

    private static readonly string[] SuccessStatuses =
        { "OK", "APPLIED", "REVERTED", "DONE", "SUCCESS", "RESTARTED" };

    public static LogEntry Parse(string line)
    {
        var m = LinePattern().Match(line);
        if (!m.Success)
            return new LogEntry { Detail = line };

        var status = m.Groups["s"].Value;
        return new LogEntry
        {
            Time = m.Groups["t"].Value,
            Action = m.Groups["a"].Value,
            Status = status,
            Detail = m.Groups["d"].Value,
            Level = Classify(status),
        };
    }

    private static LogLevel Classify(string status)
    {
        if (ErrorStatuses.Any(s => status.Equals(s, StringComparison.OrdinalIgnoreCase)))
            return LogLevel.Error;
        if (SuccessStatuses.Any(s => status.Equals(s, StringComparison.OrdinalIgnoreCase)))
            return LogLevel.Success;
        return LogLevel.Info;
    }
}
