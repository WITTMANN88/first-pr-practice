using System.Windows;
using System.Windows.Threading;
using Stakeout.Core;

namespace Stakeout;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Bring up encrypted logging first so everything after is recorded.
        Logger.Init();
        Logger.Log("App", "START", PrivilegeHelper.IsElevated() ? "elevated" : "NOT elevated");

        // Last-chance handler: log unhandled UI-thread exceptions and keep running
        // where possible instead of crashing to desktop.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.LogError("AppDomain", ex);
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.LogError("Dispatcher", e.Exception);
        MessageBox.Show(
            "Произошла ошибка. Подробности записаны в зашифрованный лог.\n\n" + e.Exception.Message,
            "STAKEOUT", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the app alive
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log("App", "EXIT");
        base.OnExit(e);
    }
}
