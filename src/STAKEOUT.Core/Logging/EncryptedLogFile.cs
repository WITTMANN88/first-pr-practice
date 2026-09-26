using System.Globalization;
using System.Text;

namespace Stakeout.Core;

/// <summary>
/// Append-only log file where every line is independently encrypted with
/// <see cref="LogCipher"/>. Per-line framing keeps appends cheap (no rewrite of
/// the whole file) while the content stays encrypted at rest.
/// Thread-safe: appends and reads are serialized on one lock.
/// </summary>
public sealed class EncryptedLogFile
{
    private readonly object _gate = new();
    private readonly LogCipher _cipher;

    public EncryptedLogFile(string path, LogCipher cipher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
    }

    public string Path { get; }

    /// <summary>
    /// Plaintext line format: "[HH:mm:ss] [Action] [Status] detail".
    /// Line breaks inside any field are flattened to spaces so one entry is always
    /// one line (the viewer's parser relies on it).
    /// </summary>
    public static string FormatLine(DateTime time, string action, string status, string? detail)
    {
        var line = $"[{time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] [{Flatten(action)}] [{Flatten(status)}]";
        return string.IsNullOrWhiteSpace(detail) ? line : $"{line} {Flatten(detail)}";
    }

    public void Append(string plaintextLine)
    {
        var encrypted = _cipher.Encrypt(plaintextLine);
        lock (_gate)
        {
            File.AppendAllText(Path, encrypted + Environment.NewLine, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Decrypt every line. Lines that fail authentication (tampered, truncated or
    /// written with another key) are skipped rather than aborting the read.
    /// </summary>
    public IReadOnlyList<string> ReadAll()
    {
        string[] lines;
        lock (_gate)
        {
            if (!File.Exists(Path)) return Array.Empty<string>();
            lines = File.ReadAllLines(Path, Encoding.UTF8);
        }

        var result = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (_cipher.TryDecrypt(line.Trim(), out var plain)) result.Add(plain);
        }
        return result;
    }

    private static string Flatten(string s)
        => s.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
}
