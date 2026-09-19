namespace Observation.Core.SystemAccess;

/// <summary>
/// Абстракция запуска внешних процессов (PowerShell-командлеты, DISM, bcdedit, netsh,
/// powercfg — см. «Исполнение и системный доступ» в плане) — как IRegistryAccessor,
/// но для команд, чтобы обработчики можно было тестировать без реального запуска процессов.
/// </summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
