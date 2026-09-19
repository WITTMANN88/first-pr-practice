using System.Diagnostics;

namespace Observation.App.Services;

/// <summary>
/// Запуск системных GUI-инструментов (devmgmt.msc, rstrui.exe и т.п.) и открытие ссылок в
/// браузере по умолчанию — через UseShellExecute=true, не через ICommandRunner: те инструменты
/// не завершаются сразу и не возвращают код/вывод для проверки, ICommandRunner же ждёт
/// WaitForExitAsync (см. ProcessCommandRunner) и заблокировал бы поток до закрытия открытого окна.
/// </summary>
public static class ShellLauncher
{
    public static void Launch(string target, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target)
            {
                Arguments = arguments ?? "",
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Инструмент недоступен на этой машине/сборке Windows — тихо игнорируем, кнопка
            // просто ничего не откроет, без модального окна с ошибкой ради второстепенного действия.
        }
    }

    public static void SearchOnline(string query) =>
        Launch("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
}
