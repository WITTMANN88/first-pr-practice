using System.IO;
using Observation.Core.Presets;
using Observation.Core.Tweaks;
using Xunit;

namespace Observation.Tests;

/// <summary>
/// Загружает настоящий presets.sample.json и проверяет, что каждый id твика внутри
/// пресета реально существует в tweaks.sample.json — иначе пресет молча выставит меньше
/// тумблеров, чем заявлено, из-за опечатки в id.
/// </summary>
public class PresetDataFileTests
{
    private static string DataPath(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TweakData", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найден файл данных: {path}");

        return path;
    }

    [Fact]
    public void RealPresetsFile_LoadsWithoutErrors()
    {
        var presets = PresetLoader.LoadFromFile(DataPath("presets.sample.json"));

        Assert.NotEmpty(presets);
    }

    [Fact]
    public void RealPresetsFile_AllTweakIdsExistInTweakRegistry()
    {
        var presets = PresetLoader.LoadFromFile(DataPath("presets.sample.json"));
        var tweakIds = TweakRegistryLoader.LoadFromFile(DataPath("tweaks.sample.json"))
            .Select(t => t.Id)
            .ToHashSet();

        foreach (var preset in presets)
        {
            foreach (var tweakId in preset.TweakIds)
                Assert.True(tweakIds.Contains(tweakId), $"Пресет '{preset.Id}' ссылается на несуществующий твик '{tweakId}'");
        }
    }

    [Fact]
    public void RealPresetsFile_HasNoDuplicateIds()
    {
        var presets = PresetLoader.LoadFromFile(DataPath("presets.sample.json"));

        var ids = presets.Select(p => p.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }
}
