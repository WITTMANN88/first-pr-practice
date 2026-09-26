using Stakeout.Core;

namespace Stakeout.Tests.Common;

public class Sha256HashTests
{
    private const string Hex = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"; // SHA-256("")

    [Theory]
    [InlineData(Hex)]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("  " + Hex + "\r\n")]
    public void TryParse_AcceptsGetFileHashAndSha256sumOutput(string text)
    {
        Assert.True(Sha256Hash.TryParse(text, out var hash));
        Assert.Equal(Hex, Sha256Hash.ToHex(hash));
    }

    [Theory]
    [InlineData(Sha256Hash.Placeholder)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B85")]    // 63 digits
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B8555")]  // 65 digits
    [InlineData("G3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]   // not hex
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(Sha256Hash.TryParse(text, out var hash));
        Assert.Null(hash);
    }

    [Fact]
    public void Matches_ComparesBytes()
    {
        Assert.True(Sha256Hash.TryParse(Hex, out var a));
        Assert.True(Sha256Hash.Matches(a, (byte[])a.Clone()));
        var b = (byte[])a.Clone();
        b[31] ^= 1;
        Assert.False(Sha256Hash.Matches(a, b));
    }
}
