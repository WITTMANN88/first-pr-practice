using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Stakeout.Core;
using Stakeout.Infrastructure;
using Stakeout.Localization;

namespace Stakeout;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Encrypted logging first, so everything after is recorded.
        Logger.Init();

        // 2. Language before any UI or view model exists: everything created
        //    afterwards reads Strings in the chosen culture.
        var culture = LocalizationManager.Resolve(
            e.Args, Environment.GetEnvironmentVariable(LocalizationManager.EnvironmentVariable));
        LocalizationManager.Apply(culture);
        // Make XAML bindings (StringFormat, number/date formatting) follow it too.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        Logger.Log("App", "START",
            $"{(PrivilegeHelper.IsElevated() ? "elevated" : "NOT elevated")}; ui={culture.Name}");

        // 3. Last-chance handlers: log and keep running where possible.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Logger.LogError("AppDomain", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.LogError("UnobservedTask", args.Exception);
            args.SetObserved();
        };

        // 4. Composition root → main window. A failure here (e.g. a broken
        //    registration) must not end as a silent crash: log, tell, exit.
        try
        {
            _services = ServiceRegistration.Build(Dispatcher);
            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Logger.LogError("Startup", ex);
            MessageBox.Show(string.Format(CultureInfo.CurrentCulture, Strings.App_StartupFailed, ex.Message),
                "STAKEOUT", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.LogError("Dispatcher", e.Exception);
        var message = string.Format(CultureInfo.CurrentCulture, Strings.App_UnhandledError, e.Exception.Message);

        // Prefer a non-blocking toast; fall back to a MessageBox if the UI
        // infrastructure is not up yet.
        var notify = _services?.GetService<INotificationService>();
        if (notify != null) notify.Error(message);
        else MessageBox.Show(message, "STAKEOUT", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true; // keep the app alive
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log("App", "EXIT");
        _services?.Dispose(); // disposes singletons: LHM sensors, notification timers
        base.OnExit(e);
    }
}
