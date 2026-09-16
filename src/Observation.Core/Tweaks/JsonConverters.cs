using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Observation.Core.Tweaks;

/// <summary>
/// Сериализует enum как kebab-case строку (restore-previous, cpu-not-in-list и т.п.) —
/// формат, которым уже пользуется схема твика в плане, вместо PascalCase-имён C#.
/// </summary>
public sealed class KebabCaseEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToKebab(value.ToString()), raw, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        throw new JsonException($"Неизвестное значение {typeof(TEnum).Name}: '{raw}'");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToKebab(value.ToString()));

    private static string ToKebab(string pascalCase)
    {
        var chars = pascalCase.SelectMany((c, i) => i > 0 && char.IsUpper(c) ? new[] { '-', c } : new[] { c });
        return new string(chars.ToArray()).ToLowerInvariant();
    }
}

/// <summary>
/// Маппинг сокращений реестровых кустов ("HKCU"/"HKLM"/"HKU"/"HKCR"/"HKCC"), которыми
/// пользуется JSON-схема твика, на Microsoft.Win32.RegistryHive.
/// </summary>
public sealed class RegistryHiveJsonConverter : JsonConverter<RegistryHive>
{
    private static readonly Dictionary<string, RegistryHive> ByAbbreviation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCU"] = RegistryHive.CurrentUser,
        ["HKLM"] = RegistryHive.LocalMachine,
        ["HKCR"] = RegistryHive.ClassesRoot,
        ["HKU"] = RegistryHive.Users,
        ["HKCC"] = RegistryHive.CurrentConfig
    };

    private static readonly Dictionary<RegistryHive, string> ToAbbreviation =
        ByAbbreviation.ToDictionary(kv => kv.Value, kv => kv.Key);

    public override RegistryHive Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? throw new JsonException("Куст реестра не указан");
        if (ByAbbreviation.TryGetValue(raw, out var hive))
            return hive;

        throw new JsonException($"Неизвестный куст реестра: '{raw}' (ожидались HKCU/HKLM/HKCR/HKU/HKCC)");
    }

    public override void Write(Utf8JsonWriter writer, RegistryHive value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToAbbreviation.TryGetValue(value, out var abbr) ? abbr : value.ToString());
}
