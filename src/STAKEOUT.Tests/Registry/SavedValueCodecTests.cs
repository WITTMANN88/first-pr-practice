using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.Tests.Registry;

public class SavedValueCodecTests
{
    public static TheoryData<RegHive, object, RegValueKind> Values => new()
    {
        { RegHive.LocalMachine, 4, RegValueKind.DWord },
        { RegHive.CurrentUser, "0", RegValueKind.String },
        { RegHive.Users, "%TEMP%\\x", RegValueKind.ExpandString },
        { RegHive.ClassesRoot, 1234567890123L, RegValueKind.QWord },
        { RegHive.CurrentConfig, new byte[] { 1, 2, 3 }, RegValueKind.Binary },
        { RegHive.LocalMachine, new[] { "a", "", "б" }, RegValueKind.MultiString },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void ExistingValue_RoundTripsThroughSavedValue(RegHive hive, object value, RegValueKind kind)
    {
        var saved = SavedValueCodec.ToSaved(hive, @"SOFTWARE\X", "Name", new RegistryValueSnapshot(value, kind, true));

        Assert.True(saved.Existed);
        Assert.True(SavedValueCodec.TryDecode(saved, out var decoded));
        var d = decoded.Value;
        Assert.Equal(hive, d.Hive);
        Assert.Equal(@"SOFTWARE\X", d.SubKey);
        Assert.Equal("Name", d.Name);
        Assert.Equal(kind, d.Kind);
        Assert.True(d.Existed);
        Assert.True(TestSupport.FakeRegistry.ValueEquals(value, d.Value!));
    }

    [Fact]
    public void AbsentValue_IsRecordedAsAbsent_AndDecodesToDelete()
    {
        var saved = SavedValueCodec.ToSaved(RegHive.CurrentUser, @"Control Panel\Mouse", "RawMouseThrottleDuration",
            RegistryValueSnapshot.Absent);

        Assert.False(saved.Existed);
        Assert.Null(saved.ValueBase64);
        Assert.True(SavedValueCodec.TryDecode(saved, out var decoded));
        Assert.False(decoded.Value.Existed);
    }

    [Theory]
    [InlineData(RegHive.LocalMachine, "HKLM")]
    [InlineData(RegHive.CurrentUser, "HKCU")]
    [InlineData(RegHive.Users, "HKU")]
    [InlineData(RegHive.ClassesRoot, "HKCR")]
    [InlineData(RegHive.CurrentConfig, "HKCC")]
    public void Hives_AreStoredByShortName(RegHive hive, string expected)
    {
        // Regression: every hive other than HKLM used to be written as "HKCU".
        Assert.Equal(expected, SavedValueCodec.ToSaved(hive, "K", "V", RegistryValueSnapshot.Absent).Hive);
    }

    [Fact]
    public void UnsupportedHive_IsRejectedAtSaveTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SavedValueCodec.HiveToString(RegHive.PerformanceData));
    }

    public static TheoryData<SavedValue> Untrustworthy => new()
    {
        new SavedValue { Hive = "HKXX", SubKey = "K", Name = "V", Existed = true, Kind = "DWord", ValueBase64 = "AQAAAA==" },
        new SavedValue { Hive = "HKLM", SubKey = "K", Name = "V", Existed = true, Kind = "Bogus", ValueBase64 = "AQAAAA==" },
        new SavedValue { Hive = "HKLM", SubKey = "K", Name = "V", Existed = true, Kind = "Unknown", ValueBase64 = "AQAAAA==" },
        new SavedValue { Hive = "HKLM", SubKey = "K", Name = "V", Existed = true, Kind = "DWord", ValueBase64 = "%%%not-base64" },
        new SavedValue { Hive = "HKLM", SubKey = "K", Name = "V", Existed = true, Kind = "DWord", ValueBase64 = "AQA=" },  // 2 bytes
        new SavedValue { Hive = "HKLM", SubKey = "K", Name = "V", Existed = true, Kind = "DWord", ValueBase64 = null },
        new SavedValue { Hive = "HKLM", SubKey = "", Name = "V", Existed = false, Kind = "Unknown" },
        new SavedValue { Hive = "HKLM", SubKey = "K", Name = "V", Existed = true, Kind = "MultiString", ValueBase64 = "/////w==" },
    };

    [Theory]
    [MemberData(nameof(Untrustworthy))]
    public void TryDecode_UntrustworthyRecord_ReturnsFalseInsteadOfGuessing(SavedValue sv)
    {
        // Regression: an unknown kind used to fall back to String and be written back as the wrong type.
        Assert.False(SavedValueCodec.TryDecode(sv, out _));
    }

    [Fact]
    public void KindNames_MatchWhatTheJsonStores()
    {
        var saved = SavedValueCodec.ToSaved(RegHive.LocalMachine, "K", "V",
            new RegistryValueSnapshot(1, RegValueKind.DWord, true));
        Assert.Equal("DWord", saved.Kind);
    }
}
