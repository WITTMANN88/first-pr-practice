using Microsoft.Win32;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Blocks Yandex software by adding its executables to the Explorer DisallowRun
/// policy. This prevents the listed processes from being launched via Explorer
/// without deleting any files. Fully reversible: the DisallowRun flag and every
/// numbered value we add are captured, so revert restores the exact prior state.
/// </summary>
public sealed class YandexBlockService : ITweak
{
    private const RegistryHive HKCU = RegistryHive.CurrentUser;
    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string ListKey = PolicyKey + @"\DisallowRun";

    // ITweak metadata so this appears as a normal row on the Tweaks page and is
    // covered by "Отменить всё".
    public string Id => "yandex";
    public string Title => "Блокировка сервисов Яндекс";
    public string Description =>
        "Блокирует запуск процессов Яндекс (браузер, обновления, Диск, Алиса) через политику DisallowRun. Файлы не удаляются.";
    public TweakCategory Category => TweakCategory.Privacy;
    public bool IsDestructive => false;
    public bool RequiresRestart => false;
    public bool RequiresExplorerRestart => false;

    // Yandex browser, updaters, Disk and Alice-related executables.
    private static readonly string[] Processes =
    {
        "browser.exe",              // Yandex Browser main process
        "Yandex.exe",
        "YandexDisk.exe",
        "YandexDisk2.exe",
        "service_update.exe",       // Yandex updater
        "yandex_browser_updater.exe",
        "SetupYandex.exe",
        "Punto.exe",                // Punto Switcher
        "YandexBrowserSetup.exe",
    };

    private readonly TweakStateStore _store;
    public YandexBlockService(TweakStateStore store) => _store = store;

    public bool IsApplied => _store.IsApplied(Id);
    public IReadOnlyList<string> BlockedProcesses => Processes;

    public Task<bool> ApplyAsync() => Task.Run(() =>
    {
        var state = _store.GetOrCreate(Id);
        state.Saved.Clear();

        // Enable the DisallowRun policy (capture prior value for revert).
        TweakOps.CaptureAndSet(state, HKCU, PolicyKey, "DisallowRun", 1, RegistryValueKind.DWord);

        // Continue numbering after any existing DisallowRun entries so we do not
        // clobber values the user already had.
        var existing = RegistryHelper.ValueNames(HKCU, ListKey);
        var index = existing.Select(n => int.TryParse(n, out var i) ? i : 0)
                            .DefaultIfEmpty(0).Max() + 1;

        foreach (var proc in Processes)
        {
            var name = index.ToString();
            // Capture (as absent) then set, so revert deletes exactly these.
            TweakOps.CaptureAndSet(state, HKCU, ListKey, name, proc, RegistryValueKind.String);
            index++;
        }

        _store.MarkApplied(Id, state);
        Logger.Log("Yandex block", "APPLIED", $"{Processes.Length} process(es)");
        return true;
    });

    public async Task<bool> RevertAsync()
    {
        var state = _store.GetOrCreate(Id);
        var ok = await TweakOps.RestoreAll(state);
        _store.MarkReverted(Id);
        Logger.Log("Yandex block", ok ? "REVERTED" : "REVERT-PARTIAL");
        return ok;
    }
}
