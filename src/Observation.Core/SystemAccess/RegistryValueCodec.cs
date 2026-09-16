using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Observation.Core.SystemAccess;

/// <summary>
/// Преобразует onData/offData из JSON-схемы твика в реальные CLR-значения для реестра,
/// и обратно — для сохранения/восстановления состояния "до" в журнале (restore-previous).
/// </summary>
public static class RegistryValueCodec
{
    public static object ToClrValue(JsonElement element, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => element.GetInt32(),
        RegistryValueKind.QWord => element.GetInt64(),
        RegistryValueKind.String or RegistryValueKind.ExpandString => element.GetString() ?? string.Empty,
        RegistryValueKind.MultiString => element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray(),
        RegistryValueKind.Binary => Convert.FromBase64String(element.GetString() ?? string.Empty),
        _ => throw new NotSupportedException($"Неподдерживаемый тип значения реестра: {kind}")
    };

    public static bool ValuesEqual(object? a, object? b)
    {
        if (a is byte[] ba && b is byte[] bb) return ba.SequenceEqual(bb);
        if (a is string[] sa && b is string[] sb) return sa.SequenceEqual(sb);
        return Equals(a, b);
    }

    /// <summary>Сериализует захваченное состояние "до" ({"existed":bool,"value":...}) для хранения в журнале.</summary>
    public static string SerializeCapturedState(bool existed, object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("existed", existed);
            writer.WritePropertyName("value");
            WriteClrValue(writer, value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static CapturedRegistryState DeserializeCapturedState(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new CapturedRegistryState(root.GetProperty("existed").GetBoolean(), root.GetProperty("value").Clone());
    }

    private static void WriteClrValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case byte[] b:
                writer.WriteStringValue(Convert.ToBase64String(b));
                break;
            case string[] arr:
                writer.WriteStartArray();
                foreach (var item in arr)
                    writer.WriteStringValue(item);
                writer.WriteEndArray();
                break;
            default:
                throw new NotSupportedException($"Не удаётся сериализовать значение типа {value.GetType()}");
        }
    }
}

/// <summary>"Value" остаётся JsonElement — реальный CLR-тип восстанавливается через ToClrValue(kind) твика.</summary>
public readonly record struct CapturedRegistryState(bool Existed, JsonElement Value);
