using System.Diagnostics;
using System.Globalization;
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
    static PowerShellScriptRunner() => Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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
    public async Task<int> RunFileAsync(string filePath, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var isBatch = filePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

        if (isBatch)
        {
            // chcp 65001 — тот же приём, что [Console]::OutputEncoding у PowerShell-ветки:
            // синхронизирует кодировку вывода cmd.exe с UTF8, которым мы декодируем ниже.
            var batchStartInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            batchStartInfo.ArgumentList.Add("/c");
            batchStartInfo.ArgumentList.Add($"chcp 65001>nul && \"{filePath}\"");
            return await RunProcessAsync(batchStartInfo, onOutputLine, cancellationToken).ConfigureAwait(false);
        }

        // Найдено вживую на реальной машине: [Console]::OutputEncoding у RunAsync/чуть выше
        // чинит только вывод скрипта, не его исходный код — Windows PowerShell 5.1 читает .ps1
        // без UTF-8 BOM активной ANSI-кодовой страницей (напр. 1251), что портит кириллицу прямо
        // в строковых литералах скрипта до какого-либо вывода. Пересохраняем без BOM-файл во
        // временную копию с явным UTF-8 BOM (сначала пробуем понять исходный текст как валидный
        // UTF-8 без BOM, иначе — как ANSI активной кодовой страницы) — и запускаем уже её.
        var runPath = EnsureUtf8Bom(filePath, out var tempCopy);
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            // Вызов через "& 'path'" в -Command сохраняет $PSScriptRoot внутри скрипта таким же,
            // как и -File — но, в отличие от -File, позволяет предварительно выставить
            // [Console]::OutputEncoding (та же кракозябра-проблема с выводом, что и в RunAsync).
            startInfo.ArgumentList.Add(
                "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '" + runPath.Replace("'", "''") + "'");

            return await RunProcessAsync(startInfo, onOutputLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (tempCopy)
                File.Delete(runPath);
        }
    }

    private static string EnsureUtf8Bom(string filePath, out bool isTempCopy)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            isTempCopy = false;
            return filePath;
        }

        string text;
        try
        {
            // Валидный UTF-8 без BOM — маловероятно получить случайно из ANSI-текста с кириллицей,
            // так что строгая проверка (throwOnInvalidBytes) — надёжный способ отличить его от ANSI.
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            var ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            text = Encoding.GetEncoding(ansiCodePage).GetString(bytes);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"observation_script_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(tempPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        isTempCopy = true;
        return tempPath;
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
