using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Observation.Core.Tweaks;

/// <summary>
/// Как твик применяется — ровно два варианта по плану («Схема твика (JSON)»):
/// одно значение реестра, либо процедурный обработчик (ITweakHandler) из Observation.Handlers.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryApplySpec), "registry")]
[JsonDerivedType(typeof(HandlerApplySpec), "handler")]
public abstract class ApplySpec
{
}

public sealed class RegistryApplySpec : ApplySpec
{
    [JsonConverter(typeof(RegistryHiveJsonConverter))]
    public required RegistryHive Hive { get; init; }

    public required string Path { get; init; }
    public required string Value { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RegistryValueKind ValueType { get; init; }
    public required JsonElement OnData { get; init; }
    public required JsonElement OffData { get; init; }
}

public sealed class HandlerApplySpec : ApplySpec
{
    public required string HandlerId { get; init; }
}
