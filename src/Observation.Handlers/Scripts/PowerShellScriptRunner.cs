using System.Diagnostics;

namespace Observation.Handlers.Scripts;

/// <summary>
/// Запуск пользовательских PowerShell-скриптов (вкладка «Скрипты») — единственное место
/// в программе, где реально запускается powershell.exe, с построчным выводом в лог интерфейса
/// (см. «Исполнение и системный доступ» в плане). Не реализует ITweakHandler — это не привязанный
/// к одному твику сервис, а свободный запуск произвольного скрипта пользователя.
/// </summary>
public sealed class PowerShellScriptRunner
{
    public async Task<int> RunAsync(string scriptText, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(scriptText);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return process.ExitCode;
    }
}
