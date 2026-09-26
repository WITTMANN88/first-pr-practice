using System.Windows;
using Stakeout.Core;

namespace Stakeout.Infrastructure;

/// <summary>
/// <see cref="IDialogService"/> backed by a MessageBox owned by the main window
/// (so it is modal to the app and centred on it). Swappable for a custom in-app
/// dialog without touching any view model.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner != null
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
