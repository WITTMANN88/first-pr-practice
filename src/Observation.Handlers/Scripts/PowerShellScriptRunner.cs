using System.Diagnostics;
using System.Text;

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
            // Без явной кодировки .NET декодирует редиректнутый вывод как Console.OutputEncoding
            // хост-процесса (OEM-кодовая страница, напр. 866 на русской Windows), а Windows
            // PowerShell 5.1 сам по себе пишет в редиректнутый stdout активной кодовой страницей
            // ANSI (напр. 1251) — несовпадение даёт «кракозябры» на кириллице (найдено вживую на
            // реальной машине). [Console]::OutputEncoding в самом скрипте синхронизирует сторону
            // записи с UTF8 здесь на стороне чтения.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + scriptText);

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
