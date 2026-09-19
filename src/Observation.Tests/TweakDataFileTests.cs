using System.IO;
using Observation.Core.Tweaks;
using Xunit;

namespace Observation.Tests;

/// <summary>
/// Загружает настоящий tweaks.sample.json (не встроенную строку JSON, как в
/// TweakRegistryLoaderTests) — ловит опечатки/несоответствия схемы в самих данных,
/// которые тест на встроенной строке не увидит.
/// </summary>
public class TweakDataFileTests
{
    private static string FindDataFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TweakData", "tweaks.sample.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найден файл данных твиков: {path}");

        return path;
    }

    [Fact]
    public void RealTweakDataFile_LoadsWithoutErrors()
    {
        var tweaks = TweakRegistryLoader.LoadFromFile(FindDataFile());

        Assert.NotEmpty(tweaks);
    }

    [Fact]
    public void RealTweakDataFile_HasNoDuplicateIds()
    {
        var tweaks = TweakRegistryLoader.LoadFromFile(FindDataFile());

        var ids = tweaks.Select(t => t.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public void RealTweakDataFile_AllHandlerIdsAreNonEmpty()
    {
        var tweaks = TweakRegistryLoader.LoadFromFile(FindDataFile());

        foreach (var tweak in tweaks)
        {
            if (tweak.Apply is HandlerApplySpec applySpec)
                Assert.False(string.IsNullOrWhiteSpace(applySpec.HandlerId), $"{tweak.Id}: пустой handlerId в apply");

            if (tweak.Verify is HandlerVerifySpec verifySpec)
                Assert.False(string.IsNullOrWhiteSpace(verifySpec.HandlerId), $"{tweak.Id}: пустой handlerId в verify");
        }
    }
}
