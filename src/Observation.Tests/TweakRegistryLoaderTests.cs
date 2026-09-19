using System.IO;
using Observation.Core.Tweaks;
using Xunit;

namespace Observation.Tests;

public class TweakRegistryLoaderTests
{
    private const string SampleJson = """
    [
      {
        "id": "perf.gamemode",
        "tab": "performance",
        "group": { "ru": "Питание и режимы", "en": "Power & modes" },
        "name": { "ru": "Режим игры (Game Mode)", "en": "Game Mode" },
        "description": { "ru": "...", "en": "..." },
        "severity": "safe",
        "controlType": "toggle",
        "requiresReboot": false,
        "applicability": { "type": "always" },
        "apply": {
          "type": "registry",
          "hive": "HKCU",
          "path": "Software\\Microsoft\\GameBar",
          "value": "AllowAutoGameMode",
          "valueType": "DWord",
          "onData": 1,
          "offData": 0
        },
        "verify": { "type": "registry-read", "matchesApply": true },
        "revert": { "type": "restore-previous" }
      },
      {
        "id": "apps.discord.hwaccel",
        "tab": "performance",
        "group": { "ru": "Приложения", "en": "Applications" },
        "name": { "ru": "Discord: аппаратное ускорение", "en": "Discord: hardware acceleration" },
        "description": { "ru": "...", "en": "..." },
        "severity": "safe",
        "controlType": "toggle",
        "requiresReboot": false,
        "apply": { "type": "handler", "handlerId": "SetDiscordHardwareAcceleration" },
        "verify": { "type": "handler", "handlerId": "SetDiscordHardwareAcceleration" },
        "revert": { "type": "restore-previous" }
      }
    ]
    """;

    [Fact]
    public void LoadFromJson_ParsesBothTweaks()
    {
        var tweaks = TweakRegistryLoader.LoadFromJson(SampleJson);

        Assert.Equal(2, tweaks.Count);
        Assert.Equal("perf.gamemode", tweaks[0].Id);
        Assert.Equal("apps.discord.hwaccel", tweaks[1].Id);
    }

    [Fact]
    public void LoadFromJson_ResolvesRegistryApplySpecPolymorphically()
    {
        var tweaks = TweakRegistryLoader.LoadFromJson(SampleJson);
        var apply = Assert.IsType<RegistryApplySpec>(tweaks[0].Apply);

        Assert.Equal("AllowAutoGameMode", apply.Value);
        Assert.Equal(1, apply.OnData.GetInt32());
        Assert.Equal(0, apply.OffData.GetInt32());
    }

    [Fact]
    public void LoadFromJson_ResolvesHandlerApplySpecPolymorphically()
    {
        var tweaks = TweakRegistryLoader.LoadFromJson(SampleJson);
        var apply = Assert.IsType<HandlerApplySpec>(tweaks[1].Apply);

        Assert.Equal("SetDiscordHardwareAcceleration", apply.HandlerId);
    }

    [Fact]
    public void LoadFromJson_ParsesKebabCaseRevertType()
    {
        var tweaks = TweakRegistryLoader.LoadFromJson(SampleJson);

        Assert.Equal(RevertType.RestorePrevious, tweaks[0].Revert.Type);
    }

    [Fact]
    public void LoadFromJson_DefaultsApplicabilityToAlways_WhenOmitted()
    {
        var tweaks = TweakRegistryLoader.LoadFromJson(SampleJson);

        Assert.Equal("always", tweaks[1].Applicability.Type);
    }

    [Fact]
    public void LoadFromDirectory_ThrowsOnDuplicateId()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "a.json"), SampleJson);
            File.WriteAllText(Path.Combine(dir.FullName, "b.json"), SampleJson);

            Assert.Throws<InvalidDataException>(() => TweakRegistryLoader.LoadFromDirectory(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
