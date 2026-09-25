using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Stakeout.Core;

/// <summary>
/// Central action log. Every tweak, download and removal is recorded.
///
/// The file lives in the system TEMP folder and is encrypted: each line is a
/// self-contained AES-256-GCM ciphertext (random nonce per line, authentication
/// tag appended), Base64-encoded. Per-line framing keeps appends cheap while
/// still encrypting the whole content at rest.
///
/// Plaintext line format (per the spec): "[Time] [Action] [Status] detail".
///
/// The key is derived with PBKDF2 from a fixed application secret bound to the
/// machine name, so the log is not portable to another machine and no key file
/// has to be stored. This is obfuscation-grade protection for local diagnostic
/// data — it is not a secret-management system.
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static readonly byte[] Key = DeriveKey();
    private static string _logPath = string.Empty;
    private static bool _initialized;

    /// <summary>Absolute path of the active encrypted log file.</summary>
    public static string LogPath => _logPath;

    /// <summary>
    /// Create (or reuse) today's log file in %TEMP%\STAKEOUT and write a header.
    /// Safe to call more than once.
    /// </summary>
    public static void Init()
    {
        lock (Gate)
        {
            if (_initialized) return;
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "STAKEOUT");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, $"stakeout-{DateTime.Now:yyyyMMdd}.log");
                _initialized = true;
            }
            catch
            {
                // If TEMP is unavailable we degrade to a no-op logger rather than
                // crashing the whole app over diagnostics.
                _initialized = false;
                return;
            }
        }

        Log("Logger", "OK", $"Session started (build {Environment.OSVersion.VersionString})");
    }

    /// <summary>Record an action with its status and an optional detail string.</summary>
    public static void Log(string action, string status, string? detail = null)
    {
        if (!_initialized) return;

        var line = $"[{DateTime.Now:HH:mm:ss}] [{action}] [{status}]" +
                   (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}");
        try
        {
            var encrypted = Encrypt(line);
            lock (Gate)
            {
                File.AppendAllText(_logPath, encrypted + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Never let logging failures propagate into the UI.
        }
    }

    /// <summary>Convenience overload for exceptions.</summary>
    public static void LogError(string action, Exception ex)
        => Log(action, "ERROR", ex.Message);

    /// <summary>
    /// Decrypt the whole log back to plaintext lines. Used by an optional
    /// in-app "view log" affordance. Lines that fail to decrypt are skipped.
    /// </summary>
    public static IEnumerable<string> ReadDecrypted()
    {
        if (!_initialized || !File.Exists(_logPath)) yield break;

        string[] lines;
        lock (Gate) { lines = File.ReadAllLines(_logPath, Encoding.UTF8); }

        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
            string? plain = null;
            try { plain = Decrypt(l); } catch { /* skip corrupt line */ }
            if (plain != null) yield return plain;
        }
    }

    // --- crypto helpers -----------------------------------------------------

    private const int NonceSize = 12; // 96-bit nonce, standard for GCM
    private const int TagSize = 16;   // 128-bit auth tag

    private static string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        // Wire layout: nonce | tag | ciphertext, Base64-encoded.
        var buffer = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, buffer, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, buffer, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, buffer, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(buffer);
    }

    private static string Decrypt(string encoded)
    {
        var buffer = Convert.FromBase64String(encoded);
        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(Key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey()
    {
        // Fixed application secret combined with the machine name. Not a
        // hard-coded password for user data — it only binds the local log to
        // this machine so it cannot be read after copying elsewhere.
        var material = "STAKEOUT::log::v1::" + Environment.MachineName;
        var salt = Encoding.UTF8.GetBytes("STAKEOUT-static-salt-2024");
        using var kdf = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(material), salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32); // AES-256
    }
}
