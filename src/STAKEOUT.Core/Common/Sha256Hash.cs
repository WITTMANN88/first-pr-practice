using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Stakeout.Core;

/// <summary>
/// Reference SHA-256 values for downloaded installers (supply-chain check).
/// Hex is compared as bytes, case-insensitively, in constant time.
/// </summary>
public static class Sha256Hash
{
    /// <summary>Catalog value for an installer whose reference hash is not known yet: installs are refused.</summary>
    public const string Placeholder = "TODO: UPDATE_HASH";

    /// <summary>
    /// Parse a 64-digit hex SHA-256 (as printed by <c>Get-FileHash</c> or
    /// <c>sha256sum</c>; surrounding whitespace ignored). Anything else, the
    /// placeholder included, is not a usable reference.
    /// </summary>
    public static bool TryParse(string? hex, [NotNullWhen(true)] out byte[]? hash)
    {
        hash = null;
        var text = hex?.Trim();
        if (text is not { Length: 64 } || !text.All(char.IsAsciiHexDigit)) return false;
        hash = Convert.FromHexString(text);
        return true;
    }

    public static bool Matches(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected)
        => CryptographicOperations.FixedTimeEquals(actual, expected);

    /// <summary>Upper-case hex, the format Get-FileHash prints.</summary>
    public static string ToHex(ReadOnlySpan<byte> hash) => Convert.ToHexString(hash);
}
