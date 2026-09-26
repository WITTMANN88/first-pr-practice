using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Stakeout.Core;

/// <summary>
/// AES-256-GCM cipher for single log lines.
///
/// Wire format of one encrypted line: Base64( nonce[12] | tag[16] | ciphertext[n] ).
/// A fresh random nonce is used for every line, so encrypting the same text
/// twice yields different output, and the GCM tag makes any modification of a
/// stored line detectable (decryption fails instead of returning garbage).
/// </summary>
public sealed class LogCipher
{
    public const int KeySize = 32;   // AES-256
    public const int NonceSize = 12; // 96-bit nonce, the GCM standard
    public const int TagSize = 16;   // 128-bit authentication tag

    // Key-derivation parameters. Changing ANY of these makes previously written
    // logs unreadable; a known-answer unit test pins them.
    private const string KeyMaterialPrefix = "STAKEOUT::log::v1::";
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("STAKEOUT-static-salt-2024");
    private const int Iterations = 100_000;

    private readonly byte[] _key;

    public LogCipher(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));
        _key = (byte[])key.Clone();
    }

    /// <summary>Cipher bound to a machine name (the default for the app's log).</summary>
    public static LogCipher ForMachine(string machineName) => new(DeriveKey(machineName));

    /// <summary>
    /// PBKDF2-SHA256 over a fixed application secret plus the machine name. This
    /// binds the log to the machine (a copied file will not decrypt elsewhere)
    /// without storing a key file. It is obfuscation-grade protection for local
    /// diagnostics, not a secret-management system.
    /// </summary>
    public static byte[] DeriveKey(string machineName)
    {
        ArgumentNullException.ThrowIfNull(machineName);
        var material = Encoding.UTF8.GetBytes(KeyMaterialPrefix + machineName);
        return Rfc2898DeriveBytes.Pbkdf2(material, Salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plain.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(buffer);
    }

    /// <summary>
    /// Decrypt one line. Throws <see cref="CryptographicException"/> if the data is
    /// truncated, was tampered with, or was encrypted with a different key, and
    /// <see cref="FormatException"/> if it is not Base64.
    /// </summary>
    public string Decrypt(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var buffer = Convert.FromBase64String(encoded);
        if (buffer.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted log line is truncated.");

        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Non-throwing variant used when reading whole files.</summary>
    public bool TryDecrypt(string encoded, [NotNullWhen(true)] out string? plaintext)
    {
        try
        {
            plaintext = Decrypt(encoded);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            plaintext = null;
            return false;
        }
    }
}
