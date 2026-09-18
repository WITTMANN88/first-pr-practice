using System.Text.Json;

namespace Observation.Core.Scripts;

/// <summary>Загружает JSON-список одноразовых скриптов — тот же подход, что TweakRegistryLoader/PresetLoader.</summary>
public static class ScriptRegistryLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<ScriptDefinition> LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }

    public static IReadOnlyList<ScriptDefinition> LoadFromJson(string json) =>
        JsonSerializer.Deserialize<List<ScriptDefinition>>(json, Options)
        ?? throw new InvalidDataException("Список скриптов пуст или не является JSON-массивом");

    public static IReadOnlyList<ScriptDefinition> LoadFromStream(Stream stream) =>
        JsonSerializer.Deserialize<List<ScriptDefinition>>(stream, Options)
        ?? throw new InvalidDataException("Список скриптов пуст или не является JSON-массивом");
}
