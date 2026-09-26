using Stakeout.Services;

namespace Stakeout.Tests.Registry;

public class RegistryValueCodecTests
{
    private static object RoundTrip(object value, RegValueKind kind)
        => RegistryValueCodec.Decode(RegistryValueCodec.Encode(value, kind), kind);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(-1)]              // how the registry API surfaces 0xFFFFFFFF
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void DWord_RoundTrips(int value)
    {
        Assert.Equal(value, RoundTrip(value, RegValueKind.DWord));
    }

    [Fact]
    public void DWord_UnsignedAllOnes_RoundTripsAsMinusOne()
    {
        // NetworkThrottlingIndex = 0xFFFFFFFF may be handed over as uint.
        Assert.Equal(-1, RoundTrip(0xFFFFFFFFu, RegValueKind.DWord));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void QWord_RoundTrips(long value)
    {
        Assert.Equal(value, RoundTrip(value, RegValueKind.QWord));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("Отменить всё")]
    [InlineData("%SystemRoot%\\System32\\drivers")]
    public void Strings_RoundTripVerbatim(string value)
    {
        Assert.Equal(value, RoundTrip(value, RegValueKind.String));
        Assert.Equal(value, RoundTrip(value, RegValueKind.ExpandString)); // %vars% stay unexpanded
    }

    public static TheoryData<string[]> MultiStrings => new()
    {
        Array.Empty<string>(),
        new[] { "" },
        new[] { "", "" },
        new[] { "a", "b", "c" },
        new[] { "browser.exe", "Юникод", "with\0nul" },
    };

    [Theory]
    [MemberData(nameof(MultiStrings))]
    public void MultiString_RoundTripsExactly(string[] value)
    {
        Assert.Equal(value, (string[])RoundTrip(value, RegValueKind.MultiString));
    }

    [Fact]
    public void MultiString_EmptyArrayAndSingleEmptyString_AreDistinct()
    {
        // Regression: joining with "\0" made [] and [""] encode identically.
        Assert.NotEqual(
            RegistryValueCodec.Encode(Array.Empty<string>(), RegValueKind.MultiString),
            RegistryValueCodec.Encode(new[] { "" }, RegValueKind.MultiString));
    }

    [Fact]
    public void Binary_RoundTrips_AndIsCopied()
    {
        var original = new byte[] { 0, 1, 2, 250, 255 };
        var encoded = RegistryValueCodec.Encode(original, RegValueKind.Binary);
        original[0] = 99; // caller mutation after encoding must not leak in

        Assert.Equal(new byte[] { 0, 1, 2, 250, 255 }, (byte[])RegistryValueCodec.Decode(encoded, RegValueKind.Binary));
        Assert.Empty((byte[])RoundTrip(Array.Empty<byte>(), RegValueKind.Binary));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Decode_DWordWithWrongLength_Throws(int length)
    {
        Assert.Throws<FormatException>(() => RegistryValueCodec.Decode(new byte[length], RegValueKind.DWord));
    }

    [Fact]
    public void Unknown_Kind_IsRejected()
    {
        Assert.Throws<NotSupportedException>(() => RegistryValueCodec.Encode(1, RegValueKind.Unknown));
        Assert.Throws<NotSupportedException>(() => RegistryValueCodec.Decode(new byte[4], RegValueKind.Unknown));
    }
}
