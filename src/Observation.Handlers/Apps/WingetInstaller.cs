using Observation.Core.SystemAccess;

namespace Observation.Handlers.Apps;

public interface IWingetInstaller
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> InstallAsync(string packageId, CancellationToken cancellationToken = default);
    Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Установка через winget — «Установка программ — техническая последовательность» в плане:
/// winget --version при старте, install с --silent (честная оговорка плана: --silent — просьба
/// к установщику, не гарантия тишины для всех программ), проверка результата через winget list,
/// не просто код возврата.
/// </summary>
public sealed class WingetInstaller : IWingetInstaller
{
    private readonly ICommandRunner _runner;

    public WingetInstaller(ICommandRunner runner) => _runner = runner;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        (await _runner.RunAsync("winget", new[] { "--version" }, cancellationToken).ConfigureAwait(false)).Succeeded;

    public Task<CommandResult> InstallAsync(string packageId, CancellationToken cancellationToken = default) =>
        _runner.RunAsync("winget", new[]
        {
            "install", "--id", packageId, "-e", "--silent",
            "--accept-package-agreements", "--accept-source-agreements"
        }, cancellationToken);

    public async Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("winget", new[] { "list", "--id", packageId, "-e" }, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.StandardOutput.Contains(packageId, StringComparison.OrdinalIgnoreCase);
    }
}
