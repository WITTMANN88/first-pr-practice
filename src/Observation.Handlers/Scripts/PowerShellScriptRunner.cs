using System.Diagnostics;
using System.Text;

namespace Observation.Handlers.Scripts;

/// <summary>
/// Запуск PowerShell-скриптов (встроенная библиотека вкладки «Скрипты», и с недавних пор —
/// пользовательские .ps1/.bat/.cmd, добавленные через файловый диалог) — единственное место
/// в программе, где реально запускается powershell.exe/cmd.exe, с построчным выводом в лог
/// интерфейса (см. «Исполнение и системный доступ» в плане). Не реализует ITweakHandler — это
/// не привязанный к одному твику сервис, а свободный запуск произвольного скрипта.
/// </summary>
public sealed class PowerShellScriptRunner
{
    /// <summary>Встроенные скрипты — инлайн-текст команды через powershell.exe -Command.</summary>
    public Task<int> RunAsync(string scriptText, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        // [Console]::OutputEncoding синхронизирует сторону записи с UTF8 на стороне чтения —
        // см. RunProcessAsync, почему это нужно (иначе кириллица превращается в "кракозябры").
        startInfo.ArgumentList.Add("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + scriptText);

        return RunProcessAsync(startInfo, onOutputLine, cancellationToken);
    }

    /// <summary>
    /// Пользовательский файл, добавленный через диалог на вкладке «Скрипты» — запускается
    /// как есть (powershell.exe -File для .ps1, чтобы $PSScriptRoot и относительные пути внутри
    /// скрипта работали правильно; cmd.exe /c для .bat/.cmd), а не инлайнится как текст.
    /// </summary>
    public Task<int> RunFileAsync(string filePath, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var isBatch = filePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

        ProcessStartInfo startInfo;
        if (isBatch)
        {
            // chcp 65001 — тот же приём, что [Console]::OutputEncoding у PowerShell-ветки:
            // синхронизирует кодировку вывода cmd.exe с UTF8, которым мы декодируем ниже.
            startInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add($"chcp 65001>nul && \"{filePath}\"");
        }
        else
        {
            startInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(filePath);
        }

        return RunProcessAsync(startInfo, onOutputLine, cancellationToken);
    }

    private static async Task<int> RunProcessAsync(ProcessStartInfo startInfo, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        // Без явной кодировки .NET декодирует редиректнутый вывод как Console.OutputEncoding
        // хост-процесса (OEM-кодовая страница, напр. 866 на русской Windows), а Windows
        // PowerShell 5.1/cmd.exe сами по себе пишут в редиректнутый stdout активной кодовой
        // страницей ANSI (напр. 1251) — несовпадение даёт «кракозябры» на кириллице (найдено
        // вживую на реальной машине).
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

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
