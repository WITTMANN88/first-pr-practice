using System.Text.Json.Serialization;

namespace Observation.Core.Tweaks;

/// <summary>
/// Как проверяется результат применения — три варианта по плану: перечитать реестр,
/// опросить состояние службы, или спросить у процедурного обработчика.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryReadVerifySpec), "registry-read")]
[JsonDerivedType(typeof(ServiceQueryVerifySpec), "service-query")]
[JsonDerivedType(typeof(HandlerVerifySpec), "handler")]
public abstract class VerifySpec
{
}

/// <summary>
/// MatchesApply=true — единственная форма, описанная в плане: перечитываем тот же
/// hive/path/value, что в ApplySpec твика, и сравниваем с ожидаемым onData/offData.
/// </summary>
public sealed class RegistryReadVerifySpec : VerifySpec
{
    public required bool MatchesApply { get; init; }
}

public sealed class ServiceQueryVerifySpec : VerifySpec
{
    public required string ServiceName { get; init; }
    public required string ExpectedStartType { get; init; }
}

public sealed class HandlerVerifySpec : VerifySpec
{
    public required string HandlerId { get; init; }
}
