using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Blocks Yandex software by adding its executables to the Explorer DisallowRun
/// policy. This prevents the listed processes from being launched via Explorer
/// without deleting any files. Applied transactionally through
/// <see cref="RegistryRollback"/>: the DisallowRun flag and every numbered value
/// we add are captured first, so revert restores the exact prior state.
/// </summary>
public sealed class YandexBlockService : ITweak
{
    private const RegHive HKCU = RegHive.CurrentUser;
    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string ListKey = PolicyKey + @"\DisallowRun";

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
    private readonly RegistryRollback _rollback;

    public YandexBlockService(TweakStateStore store, RegistryRollback rollback)
    {
        _store = store;
        _rollback = rollback;
    }

    // ITweak metadata so this appears as a normal row on the Tweaks page and is
    // covered by "Отменить всё".
    public string Id => "yandex";
    public string Title => Strings.Tweak_Yandex_Title;
    public string Description => Strings.Tweak_Yandex_Desc;
    public TweakCategory Category => TweakCategory.Privacy;
    public bool IsDestructive => false;
    public bool RequiresRestart => false;
    public bool RequiresExplorerRestart => false;
    public bool IsApplied => _store.IsApplied(Id);
    public IReadOnlyList<string> BlockedProcesses => Processes;

    public Task<bool> ApplyAsync() => Task.Run(() =>
    {
        var ops = new List<RegistryOp>
        {
            // Enable the DisallowRun policy.
            new(HKCU, PolicyKey, "DisallowRun", 1, RegValueKind.DWord),
        };

        // Continue numbering after any existing DisallowRun entries so we do not
        // clobber values the user already had. New names are captured as
        // "absent", so revert deletes exactly these.
        var index = RegistryHelper.ValueNames(HKCU, ListKey)
            .Select(n => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : 0)
            .DefaultIfEmpty(0).Max() + 1;
        foreach (var proc in Processes)
            ops.Add(new RegistryOp(HKCU, ListKey, (index++).ToString(CultureInfo.InvariantCulture), proc, RegValueKind.String));

        var state = new TweakState();
        if (!_rollback.ApplyAll(state, ops) || !_store.MarkApplied(Id, state))
        {
            _rollback.RestoreAll(state);
            Logger.Log(Id, "FAILED", "apply rolled back");
            return false;
        }
        Logger.Log(Id, "APPLIED", $"{Processes.Length} process(es)");
        return true;
    });

    public Task<bool> RevertAsync() => Task.Run(() =>
    {
        var state = _store.GetApplied(Id);
        if (state == null) return true;
        if (!_rollback.RestoreAll(state))
        {
            Logger.Log(Id, "REVERT-PARTIAL", "record kept for retry");
            return false;
        }
        _store.MarkReverted(Id);
        Logger.Log(Id, "REVERTED");
        return true;
    });
}
