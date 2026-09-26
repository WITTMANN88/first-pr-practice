using System.Management;
using System.Text.RegularExpressions;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>Outcome of "revert all".</summary>
public readonly record struct RevertAllResult(int Reverted, int Total)
{
    public bool Complete => Reverted == Total;
}

/// <summary>
/// Builds and owns the catalogue of every optimisation tweak. Each tweak is
/// reversible; the concrete apply/revert logic lives either in a generic
/// <see cref="RegistryTweak"/> or an <see cref="ActionTweak"/> for changes that
/// need powercfg / WMI / service control. All registry writes go through
/// <see cref="RegistryRollback"/>, which captures originals first.
///
/// Registry keys are documented inline. Values that are set to a specific number
/// come straight from the specification (e.g. MPO OverlayTestMode = 5,
/// NetworkThrottlingIndex = 0xFFFFFFFF, MenuShowDelay = 0).
/// Titles and descriptions come from Localization/Strings.resx.
/// </summary>
public sealed class TweakService
{
    private const RegHive HKLM = RegHive.LocalMachine;
    private const RegHive HKCU = RegHive.CurrentUser;

    /// <summary>Xbox helper services disabled by the GameDVR tweak (Start=4).</summary>
    private static readonly string[] XboxServices = { "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc" };

    /// <summary>USB "Device Parameters" flags that, when set to 0, keep a device powered.</summary>
    private static readonly string[] UsbPowerFlags =
    {
        "EnhancedPowerManagementEnabled",
        "AllowIdleIrpInD3",
        "DeviceSelectiveSuspended",
        "SelectiveSuspendEnabled",
        "SelectiveSuspendOn",
    };

    private readonly TweakStateStore _store;
    private readonly RegistryRollback _rollback;
    private readonly YandexBlockService _yandex;

    public TweakService(TweakStateStore store, RegistryRollback rollback, YandexBlockService yandex)
    {
        _store = store;
        _rollback = rollback;
        _yandex = yandex;
        Tweaks = BuildCatalog();
    }

    public IReadOnlyList<ITweak> Tweaks { get; }

    /// <summary>Backup the tweak state was restored from at startup (see TweakStateStore), or null.</summary>
    public string? StateRecoveredFrom => _store.RecoveredFrom;

    /// <summary>Revert every currently-applied tweak (drives "Отменить всё").</summary>
    public async Task<RevertAllResult> RevertAllAsync()
    {
        var applied = _store.AppliedTweakIds().ToHashSet();
        var targets = Tweaks.Where(t => applied.Contains(t.Id)).ToList();
        var reverted = 0;
        foreach (var t in targets)
            if (await t.RevertAsync()) reverted++;

        Logger.Log("RevertAll", reverted == targets.Count ? "DONE" : "PARTIAL",
            $"{reverted}/{targets.Count} tweak(s) reverted");
        return new RevertAllResult(reverted, targets.Count);
    }

    private RegistryTweak Reg(TweakInfo info, params RegistryOp[] ops) => new(_store, _rollback, info, ops);

    private ActionTweak Act(TweakInfo info, Func<TweakState, Task<bool>> apply, Func<TweakState, Task<bool>> revert)
        => new(_store, _rollback, info, apply, revert);

    /// <summary>Revert delegate for tweaks whose only side effects are registry writes.</summary>
    private Task<bool> RestoreAllAsync(TweakState s) => Task.Run(() => _rollback.RestoreAll(s));

    private List<ITweak> BuildCatalog() => new()
    {
        // ---- Privacy & telemetry ------------------------------------------

        // Block telemetry collection services WITHOUT deleting any files: set the
        // service Start type to 4 (Disabled) and the DataCollection policy to 0,
        // then stop the running service. Reverting restores the prior Start type.
        Act(new TweakInfo("diagtrack", Strings.Tweak_DiagTrack_Title, Strings.Tweak_DiagTrack_Desc, TweakCategory.Privacy),
            apply: async s =>
            {
                var ok = _rollback.CaptureAndSet(s, HKLM,
                    @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4, RegValueKind.DWord);
                ok &= _rollback.CaptureAndSet(s, HKLM,
                    @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", 4, RegValueKind.DWord);
                ok &= _rollback.CaptureAndSet(s, HKLM,
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegValueKind.DWord);
                // Stop the live service now (best-effort; failure does not fail the tweak).
                await ProcessRunner.RunAsync(SystemTools.Sc, "stop DiagTrack");
                return ok;
            },
            revert: RestoreAllAsync),

        // Advertising ID off (machine policy + current-user switch).
        Reg(new TweakInfo("advid", Strings.Tweak_AdvertisingId_Title, Strings.Tweak_AdvertisingId_Desc, TweakCategory.Privacy),
            new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, RegValueKind.DWord),
            new RegistryOp(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, RegValueKind.DWord)),

        // Cortana off via Windows Search policy.
        Reg(new TweakInfo("cortana", Strings.Tweak_Cortana_Title, Strings.Tweak_Cortana_Desc, TweakCategory.Privacy),
            new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, RegValueKind.DWord)),

        // ---- Security -----------------------------------------------------

        // UAC off. EnableLUA=0 needs a reboot to take effect.
        Reg(new TweakInfo("uac", Strings.Tweak_Uac_Title, Strings.Tweak_Uac_Desc, TweakCategory.Security,
                Destructive: true, RequiresRestart: true),
            new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0, RegValueKind.DWord)),

        // BitLocker off via WMI encryption classes (more control than manage-bde).
        Act(new TweakInfo("bitlocker", Strings.Tweak_BitLocker_Title, Strings.Tweak_BitLocker_Desc, TweakCategory.Security,
                Destructive: true),
            apply: _ => Task.Run(() => SetBitLocker(decrypt: true)),
            revert: _ => Task.Run(() => SetBitLocker(decrypt: false))),

        // ---- System -------------------------------------------------------

        // Disable forced driver updates via local group policy (revertible by hand too).
        Reg(new TweakInfo("drvupd", Strings.Tweak_DriverUpdates_Title, Strings.Tweak_DriverUpdates_Desc, TweakCategory.System),
            new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0, RegValueKind.DWord),
            new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Settings", "PreventDeviceMetadataFromNetwork", 1, RegValueKind.DWord)),

        // Hibernation off via the hidden powercfg command.
        Act(new TweakInfo("hibernate", Strings.Tweak_Hibernation_Title, Strings.Tweak_Hibernation_Desc, TweakCategory.System),
            apply: async _ => (await ProcessRunner.RunAsync(SystemTools.PowerCfg, "-h off")).Success,
            revert: async _ => (await ProcessRunner.RunAsync(SystemTools.PowerCfg, "-h on")).Success),

        // Windows animations + transparency off.
        Reg(new TweakInfo("animations", Strings.Tweak_Animations_Title, Strings.Tweak_Animations_Desc, TweakCategory.Interface,
                RequiresExplorerRestart: true),
            new RegistryOp(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, RegValueKind.DWord),
            new RegistryOp(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, RegValueKind.DWord),
            new RegistryOp(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegValueKind.String),
            new RegistryOp(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, RegValueKind.DWord)),

        // Instant context menus.
        Reg(new TweakInfo("menudelay", Strings.Tweak_MenuShowDelay_Title, Strings.Tweak_MenuShowDelay_Desc, TweakCategory.Interface,
                RequiresExplorerRestart: true),
            new RegistryOp(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "0", RegValueKind.String)),

        // ---- Performance --------------------------------------------------

        // MPO (Multi-Plane Overlay) off — OverlayTestMode=5 under Dwm.
        Reg(new TweakInfo("mpo", Strings.Tweak_Mpo_Title, Strings.Tweak_Mpo_Desc, TweakCategory.Performance,
                Destructive: true, RequiresRestart: true),
            new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, RegValueKind.DWord)),

        // Power Throttling off globally.
        Reg(new TweakInfo("powerthrottle", Strings.Tweak_PowerThrottling_Title, Strings.Tweak_PowerThrottling_Desc, TweakCategory.Performance),
            new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, RegValueKind.DWord)),

        // HAGS on — needs a reboot; UI shows the warning.
        Reg(new TweakInfo("hags", Strings.Tweak_Hags_Title, Strings.Tweak_Hags_Desc, TweakCategory.Performance,
                RequiresRestart: true),
            new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegValueKind.DWord)),

        // Network throttling off — index 0xFFFFFFFF removes the limit entirely.
        Reg(new TweakInfo("netthrottle", Strings.Tweak_NetworkThrottling_Title, Strings.Tweak_NetworkThrottling_Desc, TweakCategory.Performance),
            new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegValueKind.DWord)),

        // Custom power plan: duplicate the Ultimate Performance scheme and activate it.
        Act(new TweakInfo("powerplan", Strings.Tweak_PowerPlan_Title, Strings.Tweak_PowerPlan_Desc, TweakCategory.Performance),
            apply: ApplyPowerPlan,
            revert: RevertPowerPlan),

        // Mouse polling helper value from the guide.
        // TODO: Verify exact path on Windows build. The exact hive/subkey for
        // RawMouseThrottleDuration is not publicly documented; HKCU\Control
        // Panel\Mouse is the working assumption. This tweak is wrapped in its
        // own try/catch with verbose logging so a wrong path (or a locked key)
        // never breaks the rest of the catalogue and is traceable in the log.
        Act(new TweakInfo("rawmouse", Strings.Tweak_RawMouse_Title, Strings.Tweak_RawMouse_Desc, TweakCategory.Performance),
            apply: ApplyRawMouse,
            revert: RestoreAllAsync),

        // USB power saving off — programmatic walk of the USB device tree.
        Act(new TweakInfo("usbpower", Strings.Tweak_UsbPower_Title, Strings.Tweak_UsbPower_Desc, TweakCategory.Performance),
            apply: ApplyUsbPower,
            revert: RestoreAllAsync),

        // ---- Gaming -------------------------------------------------------

        // GameDVR + Xbox deep block.
        Act(new TweakInfo("gamedvr", Strings.Tweak_GameDvr_Title, Strings.Tweak_GameDvr_Desc, TweakCategory.Gaming),
            apply: s => Task.Run(() =>
            {
                var ok = _rollback.CaptureAndSet(s, HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0, RegValueKind.DWord);
                ok &= _rollback.CaptureAndSet(s, HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegValueKind.DWord);
                ok &= _rollback.CaptureAndSet(s, HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, RegValueKind.DWord);
                // Disable Xbox helper services (Start=4). Reverting restores prior Start.
                foreach (var svc in XboxServices)
                    ok &= _rollback.CaptureAndSet(s, HKLM, $@"SYSTEM\CurrentControlSet\Services\{svc}", "Start", 4, RegValueKind.DWord);
                return ok;
            }),
            revert: RestoreAllAsync),

        // Game Mode on.
        Reg(new TweakInfo("gamemode", Strings.Tweak_GameMode_Title, Strings.Tweak_GameMode_Desc, TweakCategory.Gaming),
            new RegistryOp(HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, RegValueKind.DWord),
            new RegistryOp(HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, RegValueKind.DWord)),

        // Mouse acceleration off.
        Reg(new TweakInfo("mouseaccel", Strings.Tweak_MouseAcceleration_Title, Strings.Tweak_MouseAcceleration_Desc, TweakCategory.Gaming),
            new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseSpeed", "0", RegValueKind.String),
            new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseThreshold1", "0", RegValueKind.String),
            new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseThreshold2", "0", RegValueKind.String)),

        // Extra guide-compatibility tweaks ("Легендарная установка", "Лучшая настройка").
        Reg(new TweakInfo("guidepack", Strings.Tweak_GuidePack_Title, Strings.Tweak_GuidePack_Desc, TweakCategory.System),
            // Favor foreground app (0x26 = short, variable, high foreground boost).
            new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26, RegValueKind.DWord),
            new RegistryOp(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, RegValueKind.DWord)),

        // ---- Applications -------------------------------------------------

        // Yandex blocking (section 3) is exposed as a normal reversible tweak.
        _yandex,
    };

    // --- BitLocker (WMI) ---------------------------------------------------

    /// <summary>
    /// Enumerate encryptable volumes and either decrypt (disable) or encrypt them.
    /// Uses root\CIMV2\Security\MicrosoftVolumeEncryption / Win32_EncryptableVolume,
    /// which gives finer control than shelling out to manage-bde.
    /// </summary>
    private static bool SetBitLocker(bool decrypt)
    {
        try
        {
            // Bound the WMI connect/enumeration so a hung provider cannot stall
            // the background task indefinitely.
            var connectOptions = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(15) };
            var scope = new ManagementScope(
                @"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption", connectOptions);
            scope.Connect();
            var query = new ObjectQuery("SELECT * FROM Win32_EncryptableVolume");
            using var searcher = new ManagementObjectSearcher(scope, query, Wmi.Options(TimeSpan.FromSeconds(15)));

            var any = false;
            Wmi.ForEach(searcher, item =>
            {
                if (item is not ManagementObject vol) return true;
                any = true;
                if (decrypt)
                {
                    // Remove key protectors then decrypt.
                    vol.InvokeMethod("DisableKeyProtectors", null);
                    vol.InvokeMethod("Decrypt", null);
                    Logger.Log("BitLocker", "DECRYPT", vol["DriveLetter"]?.ToString() ?? "?");
                }
                else
                {
                    // Best-effort re-encryption on revert.
                    vol.InvokeMethod("Encrypt", null);
                    Logger.Log("BitLocker", "ENCRYPT", vol["DriveLetter"]?.ToString() ?? "?");
                }
                return true;
            });
            return any;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log("BitLocker", "ACCESS_DENIED", ex.Message);
            return false;
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            Logger.Log("BitLocker", "ACCESS_DENIED", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError("BitLocker", ex);
            return false;
        }
    }

    // --- Power plan (powercfg) --------------------------------------------

    private const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private static async Task<bool> ApplyPowerPlan(TweakState s)
    {
        // Pre-check: the currently active scheme must be known, or revert could
        // not restore it. No known previous scheme → do not change anything.
        var current = await ProcessRunner.RunAsync(SystemTools.PowerCfg, "/getactivescheme");
        var prevGuid = ExtractGuid(current.StdOut);
        if (!current.Success || prevGuid is null)
        {
            Logger.Log("PowerPlan", "FAILED", "active scheme unknown; nothing changed");
            return false;
        }
        s.Notes["prevScheme"] = prevGuid;

        // Duplicate the Ultimate Performance scheme (a second copy is harmless)
        // and activate it.
        var dup = await ProcessRunner.RunAsync(SystemTools.PowerCfg, $"-duplicatescheme {UltimateGuid}");
        var newGuid = ExtractGuid(dup.StdOut) ?? UltimateGuid;
        s.Notes["appliedScheme"] = newGuid;

        var set = await ProcessRunner.RunAsync(SystemTools.PowerCfg, $"/setactive {newGuid}");
        return set.Success;
    }

    private static async Task<bool> RevertPowerPlan(TweakState s)
    {
        // Values come from the state file: only well-formed GUIDs reach powercfg.
        if (!s.Notes.TryGetValue("prevScheme", out var prev) || ExtractGuid(prev) != prev)
            return true; // nothing captured, nothing to restore

        var set = await ProcessRunner.RunAsync(SystemTools.PowerCfg, $"/setactive {prev}");
        if (!set.Success) return false;

        // Delete the copy this tweak created (never the built-in scheme itself),
        // so applying and reverting repeatedly does not pile up power plans.
        if (s.Notes.TryGetValue("appliedScheme", out var applied) && ExtractGuid(applied) == applied &&
            !string.Equals(applied, UltimateGuid, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(applied, prev, StringComparison.OrdinalIgnoreCase))
        {
            var deleted = await ProcessRunner.RunAsync(SystemTools.PowerCfg, $"-delete {applied}");
            Logger.Log("PowerPlan", deleted.Success ? "OK" : "WARNING", $"duplicate scheme {applied} delete exit {deleted.ExitCode}");
        }
        return true;
    }

    /// <summary>powercfg prints "Power Scheme GUID: xxxxxxxx-xxxx-... (Name)".</summary>
    private static readonly Regex GuidPattern = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? ExtractGuid(string text)
    {
        try
        {
            var m = GuidPattern.Match(text);
            return m.Success ? m.Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    // --- RawMouseThrottleDuration (guarded) --------------------------------

    /// <summary>
    /// Apply RawMouseThrottleDuration=50 with defensive error handling.
    /// TODO: Verify exact path on Windows build.
    /// </summary>
    private Task<bool> ApplyRawMouse(TweakState s) => Task.Run(() =>
    {
        const string subKey = @"Control Panel\Mouse";
        const string name = "RawMouseThrottleDuration";
        try
        {
            // Capture the prior value (or "absent") so revert is exact, then set 50.
            var ok = _rollback.CaptureAndSet(s, HKCU, subKey, name, 50, RegValueKind.DWord);
            var prior = s.Saved.Count > 0 && s.Saved[^1].Existed ? "present" : "absent";
            Logger.Log("RawMouseThrottleDuration", ok ? "APPLIED" : "FAILED",
                $@"HKCU\{subKey}\{name}=50 (prior value: {prior})");
            return ok;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log("RawMouseThrottleDuration", "ACCESS_DENIED", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError("RawMouseThrottleDuration", ex);
            return false;
        }
    });

    // --- USB power saving (registry tree walk) -----------------------------

    /// <summary>
    /// Walk HKLM\SYSTEM\CurrentControlSet\Enum\USB and clear the power-management
    /// flags in every device's "Device Parameters" sub-key. Only flags that already
    /// exist are touched (no new values are created in the device tree); each one
    /// is captured for exact rollback.
    /// </summary>
    private Task<bool> ApplyUsbPower(TweakState s) => Task.Run(() =>
    {
        const string root = @"SYSTEM\CurrentControlSet\Enum\USB";

        var touched = 0;
        foreach (var device in RegistryHelper.SubKeyNames(HKLM, root))
        {
            foreach (var instance in RegistryHelper.SubKeyNames(HKLM, $@"{root}\{device}"))
            {
                var paramPath = $@"{root}\{device}\{instance}\Device Parameters";
                foreach (var flag in UsbPowerFlags)
                    touched += _rollback.SetIfExists(s, HKLM, paramPath, flag, 0, RegValueKind.DWord) ? 1 : 0;
            }
        }

        // Also disable global USB selective suspend via the service key.
        var ok = _rollback.CaptureAndSet(s, HKLM,
            @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1, RegValueKind.DWord);

        Logger.Log("USB power", ok ? "APPLIED" : "PARTIAL", $"{touched} device flag(s) cleared");
        return ok;
    });
}
