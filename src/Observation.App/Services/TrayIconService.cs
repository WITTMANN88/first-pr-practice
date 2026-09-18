using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace Observation.App.Services;

/// <summary>
/// Трей через System.Windows.Forms.NotifyIcon — готовый компонент, план явно не требует
/// свою реализацию (см. «Хранение данных, окно, локализация»). Закрытие главного окна
/// сворачивает в трей, не завершает процесс; выход — только через явный пункт «Выход»
/// в контекстном меню (см. «Архитектура интерфейса»). Значок — тот же .ico, что и у
/// самого exe (ApplicationIcon), извлечён прямо из запущенного файла — гарантированно
/// совпадает с иконкой в проводнике/панели задач. Environment.ProcessPath, не
/// Assembly.Location — последний возвращает "" для собранного в один файл self-contained
/// exe (см. «Архитектура кода», портативная публикация), что тихо подменило бы значок трея
/// на системный по умолчанию именно в реальной публикации, а не в dev-сборке.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ILocalizationService _localization;
    private readonly Window _window;

    public TrayIconService(Window window, ILocalizationService localization)
    {
        _window = window;
        _localization = localization;

        var icon = (Environment.ProcessPath is { Length: > 0 } processPath
            ? System.Drawing.Icon.ExtractAssociatedIcon(processPath)
            : null) ?? System.Drawing.SystemIcons.Application;

        _showItem = new ToolStripMenuItem();
        _showItem.Click += (_, _) => RestoreWindow();

        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Observation",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

        UpdateMenuText();
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILocalizationService.CurrentLanguage) or "Item[]")
            UpdateMenuText();
    }

    private void UpdateMenuText()
    {
        _showItem.Text = _localization["TrayShow"];
        _exitItem.Text = _localization["TrayExit"];
    }

    private void RestoreWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static void ExitApplication()
    {
        App.IsExiting = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _localization.PropertyChanged -= OnLocalizationChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
