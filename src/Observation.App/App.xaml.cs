using System.IO;
using System.Threading;
using System.Windows;
using Observation.App.Converters;
using Observation.App.Services;
using Observation.App.ViewModels;
using Observation.Core.Tweaks;

namespace Observation.App;

public partial class App : Application
{
    // Именованный Mutex единственного экземпляра — см. «Хранение данных, окно, локализация» в плане.
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Observation.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var localization = new LocalizationService();
        Resources["Localization"] = localization;
        Resources["LocalizedText"] = new LocalizedTextConverter(localization);
        Resources["LocalizedTextMulti"] = new LocalizedTextMultiConverter(localization);

        var tweaks = LoadTweakRegistry();
        var mainViewModel = new MainWindowViewModel(localization, tweaks);

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private static IReadOnlyList<TweakDefinition> LoadTweakRegistry()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TweakData", "tweaks.sample.json");
        try
        {
            return File.Exists(path) ? TweakRegistryLoader.LoadFromFile(path) : Array.Empty<TweakDefinition>();
        }
        catch (Exception)
        {
            // Честный пустой список вкладок вместо падения на старте — диагностика ошибок
            // реестра твиков (баннеры/журнал) появится вместе с реальным движком применения.
            return Array.Empty<TweakDefinition>();
        }
    }
}
