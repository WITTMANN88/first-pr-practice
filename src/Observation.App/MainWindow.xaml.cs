using System.Windows;
using System.Windows.Input;

namespace Observation.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // DragMove() поддерживает системные жесты snap (перетаскивание к краю/Win+стрелки)
    // на WindowChrome-окнах в Windows 10/11 — не проверено визуально в этом заходе
    // (WPF не запускается на Linux), см. договорённость о проверке на реальной машине.
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximizeRestore() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
