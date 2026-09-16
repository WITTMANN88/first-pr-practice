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
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;
using Observation.Handlers.Browsers;
using Observation.Handlers.Discord;
using Observation.Handlers.OneDrive;
using Observation.Handlers.Perf;
using Observation.Handlers.Privacy;
using Observation.Handlers.Security;

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
        var library = new TweakLibrary(localization, tweaks, journal);

        // Блокирующий вызов на старте: набор твиков пока небольшой (JSON-реестр), полноценный
        // splash/async-старт — отдельная задача, не в этом проходе (подключение реального применения).
        library.ProbeInitialStatesAsync(engine).GetAwaiter().GetResult();
        HandleCrashRecovery(journal, batchRunner, tweaks);

        var mainViewModel = new MainWindowViewModel(localization, library, engine, batchRunner, conflictDetector, systemContext);

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
        ["FirewallProfiles"] = FirewallProfileHandler.Create(commandRunner)
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
            batchRunner.RevertBatchAsync(incomplete.BatchId, tweaksById).GetAwaiter().GetResult();

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
}
