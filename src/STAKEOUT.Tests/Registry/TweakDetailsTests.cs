using Stakeout.Services;

namespace Stakeout.Tests.Registry;

public class TweakDetailsTests
{
    private const RegHive HKLM = RegHive.LocalMachine;
    private const RegHive HKCU = RegHive.CurrentUser;

    [Fact]
    public void Registry_GroupsConsecutiveValuesUnderTheirKey()
    {
        var text = TweakDetails.Registry(new[]
        {
            new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, RegValueKind.DWord),
            new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseSpeed", "0", RegValueKind.String),
            new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseThreshold1", "0", RegValueKind.String),
        });

        Assert.Equal(
            "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo\n" +
            "  DisabledByGroupPolicy = 1\n" +
            "HKCU\\Control Panel\\Mouse\n" +
            "  MouseSpeed = \"0\"\n" +
            "  MouseThreshold1 = \"0\"",
            text);
    }

    [Theory]
    [InlineData(5, "5")]
    [InlineData(0x26, "38")]
    [InlineData(unchecked((int)0xFFFFFFFF), "0xFFFFFFFF")] // NetworkThrottlingIndex: not "-1"
    [InlineData(0x10000, "0x00010000")]
    public void FormatValue_DWord_IsUnsigned(int value, string expected)
    {
        Assert.Equal(expected, TweakDetails.FormatValue(value, RegValueKind.DWord));
    }

    [Fact]
    public void FormatValue_String_IsQuoted()
    {
        Assert.Equal("\"browser.exe\"", TweakDetails.FormatValue("browser.exe", RegValueKind.String));
    }

    [Theory]
    [InlineData(RegHive.LocalMachine, "HKLM")]
    [InlineData(RegHive.CurrentUser, "HKCU")]
    [InlineData(RegHive.Users, "HKU")]
    [InlineData(RegHive.ClassesRoot, "HKCR")]
    [InlineData(RegHive.CurrentConfig, "HKCC")]
    public void HiveName_IsConventionalShortForm(RegHive hive, string expected)
    {
        Assert.Equal(expected, TweakDetails.HiveName(hive));
    }

    [Fact]
    public void Key_And_Join_BuildSections()
    {
        var text = TweakDetails.Join(
            TweakDetails.Key(HKLM, @"SYSTEM\CurrentControlSet\Enum\USB\*\*\Device Parameters", "AllowIdleIrpInD3 = 0"),
            "",
            "powercfg -h off");

        Assert.Equal(
            "HKLM\\SYSTEM\\CurrentControlSet\\Enum\\USB\\*\\*\\Device Parameters\n  AllowIdleIrpInD3 = 0\n\npowercfg -h off",
            text);
    }
}
