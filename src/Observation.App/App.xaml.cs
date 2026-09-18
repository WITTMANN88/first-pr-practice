using System.IO;
using System.Threading;
using System.Windows;
using Observation.App.Converters;
using Observation.App.Runtime;
using Observation.App.Services;
using Observation.App.ViewModels;
using Observation.Core.Batch;
using Observation.Core.Conflicts;
using Observation.Core.Engine;
using Observation.Core.Handlers;
using Observation.Core.Journal;
using Observation.Core.Presets;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;
using Observation.Handlers;
using Observation.Handlers.Apps;
using Observation.Handlers.Browsers;
using Observation.Handlers.Debloat;
using Observation.Handlers.Discord;
using Observation.Handlers.OneDrive;
using Observation.Handlers.Perf;
using Observation.Handlers.Privacy;
using Observation.Handlers.Security;
using Observation.Handlers.Updates;

namespace Observation.App;

public partial class App : Application
{
    // Именованный Mutex единственного экземпляра — см. «Хранение данных, окно, локализация» в плане.
    private Mutex? _singleInstanceMutex;
    private TrayIconService? _trayIcon;

    /// <summary>
    /// true только когда выход инициирован явно (пункт «Выход» в трее) — иначе закрытие
    /// главного окна (крестик) сворачивает в трей, не завершает процесс (см. «Архитектура
    /// интерфейса» в плане). Устанавливается TrayIconService перед Shutdown().
    /// </summary>
    public static bool IsExiting { get; set; }

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

        // Композиция движка — см. «Архитектура кода» в плане. Прямой доступ к реестру
        // (Microsoft.Win32.Registry), без промежуточного PowerShell-хоста.
        var registry = new Win32RegistryAccessor();
        var commandRunner = new ProcessCommandRunner();
        var systemContext = new WindowsSystemContextProvider(registry).GetCurrent();
        var handlers = BuildHandlers(registry, commandRunner);
        var engine = new TweakEngine(registry, handlers);
        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Observation_Data");
        var journal = new JsonLinesJournalStore(dataFolder);
        var batchRunner = new BatchRunner(engine, journal);
        var conflictDetector = new ConflictDetector();

        var tweaks = LoadTweakRegistry();
        var presets = LoadPresets();
        var library = new TweakLibrary(localization, tweaks, journal, presets);
        var pcSpecsProvider = new PcSpecsProvider();

        // Блокирующий вызов на старте: набор твиков пока небольшой (JSON-реестр), полноценный
        // splash/async-старт — отдельная задача, не в этом проходе (подключение реального применения).
        // Task.Run — обязателен: без него continuation после реального await (PowerShellToggleHandler/
        // ProcessCommandRunner для CFA/Firewall) пытается вернуться в захваченный DispatcherSynchronizationContext,
        // а поток UI уже заблокирован на GetResult() — глухой deadlock до показа окна.
        Task.Run(() => library.ProbeInitialStatesAsync(engine)).GetAwaiter().GetResult();
        HandleCrashRecovery(journal, batchRunner, tweaks);

        var pcSpecs = pcSpecsProvider.GetCurrent();
        var uwpScanner = new PowerShellUwpPackageScanner(commandRunner);
        var wingetInstaller = new WingetInstaller(commandRunner);
        var mainViewModel = new MainWindowViewModel(localization, library, engine, batchRunner, conflictDetector, systemContext,
            journal, pcSpecs, uwpScanner, wingetInstaller);

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Closing += MainWindow_Closing;
        window.Show();

        _trayIcon = new TrayIconService(window, localization);
    }

    /// <summary>Крестик сворачивает в трей вместо завершения процесса — единственный явный выход отсюда — пункт «Выход» в трее (TrayIconService.ExitApplication).</summary>
    private static void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsExiting)
            return;

        e.Cancel = true;
        ((Window)sender!).Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private static Dictionary<string, ITweakHandler> BuildHandlers(IRegistryAccessor registry, ICommandRunner commandRunner) => new()
    {
        ["GameModeToggle"] = GameModeHandler.Create(registry),
        ["SetDiscordHardwareAcceleration"] = new DiscordHardwareAccelerationHandler(),
        ["BraveDebloat"] = BraveDebloatHandler.Create(registry),
        ["EdgeDebloat"] = EdgeDebloatHandler.Create(registry),
        ["RemoveOneDrive"] = new OneDriveRemovalHandler(registry),
        ["XboxGameBarOff"] = XboxGameBarHandler.Create(registry),
        ["BrowserPerformance"] = BrowserPerformanceHandler.Create(registry),
        ["UacSliderMin"] = UacSliderHandler.Create(registry),
        ["WindowsCopilotOff"] = WindowsCopilotHandler.Create(registry),
        ["ActivityHistoryOff"] = ActivityHistoryHandler.Create(registry),
        ["WebSearchOff"] = WebSearchHandler.Create(registry),
        ["ControlledFolderAccess"] = ControlledFolderAccessHandler.Create(commandRunner),
        ["FirewallProfiles"] = FirewallProfileHandler.Create(commandRunner),
        ["DeferUpdates"] = DeferUpdatesHandler.Create(registry),
        ["DiagTrackOff"] = DiagTrackHandler.Create(commandRunner),
        ["FeatureNetFx3"] = WindowsOptionalFeatureHandler.Create(commandRunner, "NetFx3"),
        ["FeatureHyperV"] = WindowsOptionalFeatureHandler.Create(commandRunner, "Microsoft-Hyper-V-All"),
        ["FeatureWsl"] = WindowsOptionalFeatureHandler.Create(commandRunner, "Microsoft-Windows-Subsystem-Linux"),
        ["FeatureNfsClient"] = WindowsOptionalFeatureHandler.Create(commandRunner, "ServicesForNFS-ClientOnly"),
        ["FeatureSandbox"] = WindowsOptionalFeatureHandler.Create(commandRunner, "Containers-DisposableClientVM"),
        ["FeatureLegacyMedia"] = new PowerShellToggleHandler(commandRunner,
            onScript: "Enable-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer -All -NoRestart -ErrorAction Stop | Out-Null; " +
                      "Enable-WindowsOptionalFeature -Online -FeatureName DirectPlay -All -NoRestart -ErrorAction Stop | Out-Null",
            offScript: "Disable-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer -NoRestart -ErrorAction Stop | Out-Null; " +
                       "Disable-WindowsOptionalFeature -Online -FeatureName DirectPlay -NoRestart -ErrorAction Stop | Out-Null",
            verifyScript: "(Get-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer).State -eq 'Enabled'"),
        ["CapabilityOpenSshServer"] = WindowsCapabilityHandler.Create(commandRunner, "OpenSSH.Server~~~~0.0.1.0"),
        ["CapabilityOpenSshClient"] = WindowsCapabilityHandler.Create(commandRunner, "OpenSSH.Client~~~~0.0.1.0")
    };

    /// <summary>
    /// Механизм надёжности №3 из плана: заголовок пакета со статусом InProgress без
    /// последующего Completed/AcknowledgedIncomplete — признак сбоя приложения. Здесь —
    /// упрощённый диалог из двух исходов (откатить/оставить как есть) вместо трёх кнопок
    /// макета: "доделать оставшиеся" потребовал бы отдельного API в BatchRunner для
    /// возобновления конкретно недостающих твиков пакета — не в этом проходе.
    /// </summary>
    private static void HandleCrashRecovery(IJournalStore journal, BatchRunner batchRunner, IReadOnlyList<TweakDefinition> tweaks)
    {
        var incomplete = journal.FindIncompleteBatch();
        if (incomplete is null)
            return;

        var tweaksById = tweaks.ToDictionary(t => t.Id);
        var revert = MessageBox.Show(
            $"Обнаружен незавершённый пакет применения от {incomplete.StartedAt:g}: " +
            $"применено {incomplete.TweaksApplied} из {incomplete.TweaksPlanned}.\n\n" +
            "Откатить уже применённые изменения этого пакета?",
            "Незавершённый пакет", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (revert == MessageBoxResult.Yes)
            Task.Run(() => batchRunner.RevertBatchAsync(incomplete.BatchId, tweaksById)).GetAwaiter().GetResult();

        journal.AcknowledgeIncomplete(incomplete.BatchId);
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
            // Честный пустой список вкладок вместо падения на старте.
            return Array.Empty<TweakDefinition>();
        }
    }

    private static IReadOnlyList<PresetDefinition> LoadPresets()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TweakData", "presets.sample.json");
        try
        {
            return File.Exists(path) ? PresetLoader.LoadFromFile(path) : Array.Empty<PresetDefinition>();
        }
        catch (Exception)
        {
            // Честный пустой список пресетов вместо падения на старте.
            return Array.Empty<PresetDefinition>();
        }
    }
}
