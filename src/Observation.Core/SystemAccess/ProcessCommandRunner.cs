using System.Diagnostics;

namespace Observation.Core.SystemAccess;

/// <summary>Реальный запуск через Process.Start — единственная реализация ICommandRunner вне тестов.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new CommandResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}
