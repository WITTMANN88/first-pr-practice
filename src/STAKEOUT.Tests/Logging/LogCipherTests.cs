using System.Security.Cryptography;
using System.Text;
using Stakeout.Core;

namespace Stakeout.Tests.Logging;

public class LogCipherTests
{
    private static readonly LogCipher Cipher = LogCipher.ForMachine("TEST-PC");

    // PBKDF2-SHA256("STAKEOUT::log::v1::TEST-PC", "STAKEOUT-static-salt-2024", 100000, 32),
    // computed independently with Python hashlib and Node crypto (both agree).
    private const string KnownKeyHex = "89fd1ef7f6d514cd955f1b372eda03e3cbb006e0834cf9bd1d163e16cf611862";

    // AES-256-GCM of the plaintext below under the known key with nonce 00..0B,
    // produced by Node crypto and laid out as base64(nonce | tag | ciphertext).
    private const string NodeVector =
        "AAECAwQFBgcICQoLoBOxk6Wsdoj1U7U9AMMcmz2ORtYE1c8EzhYuy4ubN5+GSNGCVhWQbYsmRSUQsl4fG4gX7OoHZrDBX30=";
    private const string NodeVectorPlaintext = "[12:34:56] [UAC] [APPLIED] проверка";

    [Fact]
    public void DeriveKey_MatchesIndependentKnownAnswer()
    {
        // Pins every KDF parameter: changing any would make existing logs unreadable.
        Assert.Equal(KnownKeyHex, Convert.ToHexString(LogCipher.DeriveKey("TEST-PC")), ignoreCase: true);
    }

    [Fact]
    public void Decrypt_VectorFromIndependentImplementation_Succeeds()
    {
        // Proves the on-disk layout (nonce | tag | ciphertext) and AES-GCM usage are standard.
        Assert.Equal(NodeVectorPlaintext, Cipher.Decrypt(NodeVector));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[10:00:00] [Logger] [OK] Session started")]
    [InlineData("Кириллица: «Отменить всё» — готово")]
    [InlineData("emoji 💀🗡️ and \t tabs")]
    [InlineData("line one\nline two\r\nline three")]
    public void EncryptThenDecrypt_ReturnsOriginal(string plaintext)
    {
        Assert.Equal(plaintext, Cipher.Decrypt(Cipher.Encrypt(plaintext)));
    }

    [Fact]
    public void EncryptThenDecrypt_LargePayload_ReturnsOriginal()
    {
        var big = string.Concat(Enumerable.Repeat("0123456789абвгд", 10_000));
        Assert.Equal(big, Cipher.Decrypt(Cipher.Encrypt(big)));
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_UsesFreshNonce()
    {
        var a = Cipher.Encrypt("same text");
        var b = Cipher.Encrypt("same text");

        Assert.NotEqual(a, b);
        Assert.NotEqual(
            Convert.FromBase64String(a).AsSpan(0, LogCipher.NonceSize).ToArray(),
            Convert.FromBase64String(b).AsSpan(0, LogCipher.NonceSize).ToArray());
    }

    [Fact]
    public void Encrypt_WireFormat_IsNonceTagCiphertext()
    {
        const string text = "проверка"; // 16 UTF-8 bytes
        var raw = Convert.FromBase64String(Cipher.Encrypt(text));
        Assert.Equal(LogCipher.NonceSize + LogCipher.TagSize + Encoding.UTF8.GetByteCount(text), raw.Length);
    }

    [Fact]
    public void Encrypt_DoesNotContainPlaintext()
    {
        var encrypted = Cipher.Encrypt("ACCESS_DENIED secret-path");
        Assert.DoesNotContain("ACCESS_DENIED", encrypted, StringComparison.Ordinal);
        Assert.DoesNotContain("ACCESS_DENIED", Encoding.UTF8.GetString(Convert.FromBase64String(encrypted)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]   // nonce
    [InlineData(11)]  // last nonce byte
    [InlineData(12)]  // first tag byte
    [InlineData(27)]  // last tag byte
    [InlineData(28)]  // first ciphertext byte
    public void Decrypt_AnySingleBitFlip_IsDetected(int index)
    {
        var raw = Convert.FromBase64String(Cipher.Encrypt("[01:02:03] [MPO] [APPLIED]"));
        raw[index] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => Cipher.Decrypt(Convert.ToBase64String(raw)));
    }

    [Fact]
    public void Decrypt_WithKeyOfAnotherMachine_Fails()
    {
        var encrypted = Cipher.Encrypt("bound to TEST-PC");
        var other = LogCipher.ForMachine("OTHER-PC");
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_TruncatedInput_ThrowsCryptographicException()
    {
        var tooShort = Convert.ToBase64String(new byte[LogCipher.NonceSize + LogCipher.TagSize - 1]);
        Assert.ThrowsAny<CryptographicException>(() => Cipher.Decrypt(tooShort));
    }

    [Fact]
    public void Decrypt_NotBase64_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Cipher.Decrypt("not base64 !!!"));
    }

    [Theory]
    [InlineData("not base64 !!!")]
    [InlineData("AAAA")]
    [InlineData("")]
    public void TryDecrypt_InvalidInput_ReturnsFalseWithoutThrowing(string input)
    {
        Assert.False(Cipher.TryDecrypt(input, out var plain));
        Assert.Null(plain);
    }

    [Fact]
    public void DeriveKey_IsDeterministic_AndMachineSpecific()
    {
        Assert.Equal(LogCipher.DeriveKey("PC-A"), LogCipher.DeriveKey("PC-A"));
        Assert.NotEqual(LogCipher.DeriveKey("PC-A"), LogCipher.DeriveKey("PC-B"));
        Assert.Equal(LogCipher.KeySize, LogCipher.DeriveKey("PC-A").Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_RejectsWrongKeyLength(int length)
    {
        Assert.Throws<ArgumentException>(() => new LogCipher(new byte[length]));
    }

    [Fact]
    public void Constructor_CopiesKey_SoCallerMutationHasNoEffect()
    {
        var key = LogCipher.DeriveKey("TEST-PC");
        var cipher = new LogCipher(key);
        var encrypted = cipher.Encrypt("x");
        Array.Clear(key);
        Assert.Equal("x", cipher.Decrypt(encrypted));
    }
}
