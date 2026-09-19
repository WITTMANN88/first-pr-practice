using System.Diagnostics;
using System.Text;

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

        // winget сам пишет в редиректнутый stdout/stderr в UTF-8, а без явной кодировки .NET
        // декодирует его ANSI-кодовой страницей хоста — несовпадение даёт «кракозябры» на
        // кириллице (найдено вживую: сообщение об ошибке winget install). Это НЕ применимо ко
        // всем остальным вызовам через этот же RunAsync (почти все — powershell.exe): без своего
        // "[Console]::OutputEncoding = UTF8" внутри самого скрипта (как делает отдельный
        // PowerShellScriptRunner для вкладки «Скрипты») Windows PowerShell 5.1 пишет
        // редиректнутый вывод/ошибки ANSI-кодовой страницей хоста, а не UTF-8 — форсировать
        // здесь UTF-8 сломало бы уже корректно работающие кириллические сообщения об ошибках
        // PowerShell-обработчиков (UAC/CFA/firewall/UWP-скан и т.д.), поэтому кодировка задаётся
        // только для winget, по имени исполняемого файла.
        if (fileName.Equals("winget", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("winget.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

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
