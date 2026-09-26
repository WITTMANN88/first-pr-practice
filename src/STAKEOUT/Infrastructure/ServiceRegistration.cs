using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Stakeout.Core;
using Stakeout.Services;
using Stakeout.ViewModels;

namespace Stakeout.Infrastructure;

/// <summary>
/// Composition root: the one place where interfaces are bound to
/// implementations. View models and services receive their dependencies through
/// constructors and never create or look up services themselves.
/// </summary>
public static class ServiceRegistration
{
    public static ServiceProvider Build(Dispatcher uiDispatcher)
    {
        var services = new ServiceCollection();

        // WPF / Windows implementations of core abstractions.
        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(uiDispatcher));
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IRegistryAccess, WindowsRegistryAccess>();

        // Notifications: one instance behind the sending and presenting interfaces.
        services.AddSingleton(sp => new NotificationService(sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<INotificationFeed>(sp => sp.GetRequiredService<NotificationService>());

        // Services.
        services.AddSingleton(_ => new TweakStateStore(TweakStateStore.DefaultPath));
        services.AddSingleton<RegistryRollback>();
        services.AddSingleton<YandexBlockService>();
        services.AddSingleton<TweakService>();
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<IUwpService, UwpService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<SoftwareInstallService>();

        // View models (one instance each: pages keep their state across navigation).
        services.AddSingleton<SysInfoViewModel>();
        services.AddSingleton<TweaksViewModel>();
        services.AddSingleton<UwpViewModel>();
        services.AddSingleton<SoftwareViewModel>();
        services.AddSingleton<LogViewerViewModel>();
        services.AddSingleton<MainViewModel>();

        // Shell.
        services.AddSingleton<MainWindow>();

        // Fail fast at startup on a missing or mis-typed registration.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
