using System.Text.Json;

namespace Observation.Core.Presets;

/// <summary>Загружает JSON-список пресетов — тот же подход, что и TweakRegistryLoader.</summary>
public static class PresetLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<PresetDefinition> LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }

    public static IReadOnlyList<PresetDefinition> LoadFromJson(string json) =>
        JsonSerializer.Deserialize<List<PresetDefinition>>(json, Options)
        ?? throw new InvalidDataException("Список пресетов пуст или не является JSON-массивом");

    public static IReadOnlyList<PresetDefinition> LoadFromStream(Stream stream) =>
        JsonSerializer.Deserialize<List<PresetDefinition>>(stream, Options)
        ?? throw new InvalidDataException("Список пресетов пуст или не является JSON-массивом");
}
