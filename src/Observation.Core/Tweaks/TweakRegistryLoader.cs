using System.Text.Json;

namespace Observation.Core.Tweaks;

/// <summary>
/// Загружает JSON-реестр твиков (массив TweakDefinition) — "новая функция = запись
/// в реестре, а не новый код UI" (приём WinUtil, см. «Журнал решений»).
/// </summary>
public static class TweakRegistryLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<TweakDefinition> LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }

    public static IReadOnlyList<TweakDefinition> LoadFromJson(string json) =>
        JsonSerializer.Deserialize<List<TweakDefinition>>(json, Options)
        ?? throw new InvalidDataException("Реестр твиков пуст или не является JSON-массивом");

    public static IReadOnlyList<TweakDefinition> LoadFromStream(Stream stream) =>
        JsonSerializer.Deserialize<List<TweakDefinition>>(stream, Options)
        ?? throw new InvalidDataException("Реестр твиков пуст или не является JSON-массивом");

    /// <summary>Загружает и объединяет все *.json из папки — реестр может быть разбит по вкладкам/группам.</summary>
    public static IReadOnlyList<TweakDefinition> LoadFromDirectory(string directoryPath)
    {
        var result = new List<TweakDefinition>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f))
        {
            foreach (var tweak in LoadFromFile(file))
            {
                if (!seenIds.Add(tweak.Id))
                    throw new InvalidDataException($"Повторяющийся id твика: '{tweak.Id}' (файл: {Path.GetFileName(file)})");

                result.Add(tweak);
            }
        }

        return result;
    }
}
