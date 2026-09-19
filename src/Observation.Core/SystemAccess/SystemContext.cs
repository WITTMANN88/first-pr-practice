namespace Observation.Core.SystemAccess;

/// <summary>Снимок версии/редакции Windows и модели CPU — вход для ApplicabilityRule.Evaluate.</summary>
public sealed record SystemContext(int BuildNumber, string EditionId, string DisplayVersion, string? CpuModel);

public interface ISystemContextProvider
{
    SystemContext GetCurrent();
}
