using System.Management;
using Microsoft.Win32;
using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Builds and owns the catalogue of every optimisation tweak. Each tweak is
/// reversible; the concrete apply/revert logic lives either in a generic
/// <see cref="RegistryTweak"/> or an <see cref="ActionTweak"/> for changes that
/// need powercfg / WMI / service control.
///
/// Registry keys are documented inline. Values that are set to a specific number
/// come straight from the specification (e.g. MPO OverlayTestMode = 5,
/// NetworkThrottlingIndex = 0xFFFFFFFF, MenuShowDelay = 0).
/// </summary>
public sealed class TweakService
{
    private const RegistryHive HKLM = RegistryHive.LocalMachine;
    private const RegistryHive HKCU = RegistryHive.CurrentUser;

    private readonly TweakStateStore _store;
    private readonly YandexBlockService _yandex;
    public IReadOnlyList<ITweak> Tweaks { get; }

    public TweakService(TweakStateStore store, YandexBlockService yandex)
    {
        _store = store;
        _yandex = yandex;
        Tweaks = BuildCatalog();
    }

    /// <summary>Revert every currently-applied tweak (drives "Отменить всё").</summary>
    public async Task<int> RevertAllAsync()
    {
        var applied = _store.AppliedTweakIds().ToHashSet();
        var count = 0;
        foreach (var t in Tweaks)
        {
            if (applied.Contains(t.Id))
            {
                if (await t.RevertAsync()) count++;
            }
        }
        Logger.Log("RevertAll", "DONE", $"{count} tweak(s) reverted");
        return count;
    }

    private List<ITweak> BuildCatalog()
    {
        var list = new List<ITweak>();

        // ---- Privacy & telemetry ------------------------------------------

        // Block telemetry collection services WITHOUT deleting any files: set the
        // service Start type to 4 (Disabled) and the DataCollection policy to 0,
        // then stop the running service. Reverting restores the prior Start type.
        list.Add(new ActionTweak(_store, "diagtrack",
            "Телеметрия (DiagTrack)",
            "Блокирует службы сбора данных DiagTrack и dmwappushservice и задаёт AllowTelemetry=0. Файлы не удаляются.",
            TweakCategory.Privacy,
            apply: async s =>
            {
                var ok = TweakOps.CaptureAndSet(s, HKLM,
                    @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4, RegistryValueKind.DWord);
                ok &= TweakOps.CaptureAndSet(s, HKLM,
                    @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", 4, RegistryValueKind.DWord);
                ok &= TweakOps.CaptureAndSet(s, HKLM,
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord);
                // Stop the live service now (best-effort; ignore failure).
                await ProcessRunner.RunAsync("sc.exe", "stop DiagTrack");
                return ok;
            },
            revert: TweakOps.RestoreAll));

        // Advertising ID off (machine policy + current-user switch).
        list.Add(new RegistryTweak(_store, "advid",
            "Advertising ID",
            "Отключает рекламный идентификатор для приложений.",
            TweakCategory.Privacy, new[]
            {
                new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, RegistryValueKind.DWord),
                new RegistryOp(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, RegistryValueKind.DWord),
            }));

        // Cortana off via Windows Search policy.
        list.Add(new RegistryTweak(_store, "cortana",
            "Cortana",
            "Блокирует Cortana через параметр AllowCortana в политиках Windows Search.",
            TweakCategory.Privacy, new[]
            {
                new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, RegistryValueKind.DWord),
            }));

        // ---- Security -----------------------------------------------------

        // UAC off. EnableLUA=0 needs a reboot to take effect.
        list.Add(new RegistryTweak(_store, "uac",
            "UAC (Контроль учётных записей)",
            "Отключает контроль учётных записей. Требуется перезагрузка.",
            TweakCategory.Security, new[]
            {
                new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0, RegistryValueKind.DWord),
                new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0, RegistryValueKind.DWord),
                new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0, RegistryValueKind.DWord),
            },
            destructive: true, requiresRestart: true));

        // BitLocker off via WMI encryption classes (more control than manage-bde).
        list.Add(new ActionTweak(_store, "bitlocker",
            "BitLocker",
            "Отключает шифрование BitLocker на всех томах через WMI (Win32_EncryptableVolume).",
            TweakCategory.Security,
            apply: _ => Task.Run(() => SetBitLocker(decrypt: true)),
            revert: _ => Task.Run(() => SetBitLocker(decrypt: false)),
            destructive: true, requiresRestart: false));

        // ---- System -------------------------------------------------------

        // Disable forced driver updates via local group policy (revertible by hand too).
        list.Add(new RegistryTweak(_store, "drvupd",
            "Обновление драйверов",
            "Отключает принудительное обновление драйверов через Windows Update (групповые политики).",
            TweakCategory.System, new[]
            {
                new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord),
                new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0, RegistryValueKind.DWord),
                new RegistryOp(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Settings", "PreventDeviceMetadataFromNetwork", 1, RegistryValueKind.DWord),
            }));

        // Hibernation off via the hidden powercfg command.
        list.Add(new ActionTweak(_store, "hibernate",
            "Гибернация",
            "Отключает гибернацию командой powercfg -h off (освобождает hiberfil.sys).",
            TweakCategory.System,
            apply: async _ => (await ProcessRunner.RunAsync("powercfg.exe", "-h off")).Success,
            revert: async _ => (await ProcessRunner.RunAsync("powercfg.exe", "-h on")).Success));

        // Windows animations + transparency off.
        list.Add(new RegistryTweak(_store, "animations",
            "Анимации Windows",
            "Полностью отключает анимации и прозрачность интерфейса. Требуется перезапуск проводника.",
            TweakCategory.Interface, new[]
            {
                new RegistryOp(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, RegistryValueKind.DWord),
                new RegistryOp(HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, RegistryValueKind.DWord),
                new RegistryOp(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String),
                new RegistryOp(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, RegistryValueKind.DWord),
            },
            requiresExplorerRestart: true));

        // Instant context menus.
        list.Add(new RegistryTweak(_store, "menudelay",
            "MenuShowDelay",
            "Устанавливает MenuShowDelay=0 для мгновенного отклика контекстных меню.",
            TweakCategory.Interface, new[]
            {
                new RegistryOp(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "0", RegistryValueKind.String),
            },
            requiresExplorerRestart: true));

        // ---- Performance --------------------------------------------------

        // MPO (Multi-Plane Overlay) off — OverlayTestMode=5 under Dwm.
        list.Add(new RegistryTweak(_store, "mpo",
            "MPO (Multi-Plane Overlay)",
            "Отключает MPO (OverlayTestMode=5 в ветке Dwm). Помогает от мерцания/артефактов.",
            TweakCategory.Performance, new[]
            {
                new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, RegistryValueKind.DWord),
            },
            destructive: true, requiresRestart: true));

        // Power Throttling off globally.
        list.Add(new RegistryTweak(_store, "powerthrottle",
            "Power Throttling",
            "Глобально отключает энергосбережение фоновых процессов (PowerThrottlingOff=1).",
            TweakCategory.Performance, new[]
            {
                new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, RegistryValueKind.DWord),
            }));

        // HAGS on — needs a reboot; UI shows the warning.
        list.Add(new RegistryTweak(_store, "hags",
            "HAGS (аппаратное планирование GPU)",
            "Включает Hardware-accelerated GPU Scheduling (HwSchMode=2). Требуется перезагрузка.",
            TweakCategory.Performance, new[]
            {
                new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord),
            },
            requiresRestart: true));

        // Network throttling off — index 0xFFFFFFFF removes the limit entirely.
        list.Add(new RegistryTweak(_store, "netthrottle",
            "NetworkThrottlingIndex",
            "Снимает сетевые ограничения (NetworkThrottlingIndex=FFFFFFFF).",
            TweakCategory.Performance, new[]
            {
                new RegistryOp(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord),
            }));

        // Custom power plan: duplicate the Ultimate Performance scheme and activate it.
        list.Add(new ActionTweak(_store, "powerplan",
            "План электропитания",
            "Применяет план e9a42b02-… (Ultimate Performance) с предварительной проверкой.",
            TweakCategory.Performance,
            apply: ApplyPowerPlan,
            revert: RevertPowerPlan,
            requiresRestart: false));

        // Mouse polling helper value from the guide.
        // TODO: Verify exact path on Windows build. The exact hive/subkey for
        // RawMouseThrottleDuration is not publicly documented; HKCU\Control
        // Panel\Mouse is the working assumption. This tweak is wrapped in its
        // own try/catch with verbose logging so a wrong path (or a locked key)
        // never breaks the rest of the catalogue and is traceable in the log.
        list.Add(new ActionTweak(_store, "rawmouse",
            "Опрос мыши (RawMouseThrottleDuration)",
            "Устанавливает RawMouseThrottleDuration=50 (по гайду).",
            TweakCategory.Performance,
            apply: ApplyRawMouse,
            revert: TweakOps.RestoreAll));

        // USB power saving off — programmatic walk of the USB device tree.
        list.Add(new ActionTweak(_store, "usbpower",
            "USB: энергосбережение",
            "Отключает энергосбережение всех USB-контроллеров перебором дерева реестра.",
            TweakCategory.Performance,
            apply: ApplyUsbPower,
            revert: TweakOps.RestoreAll));

        // ---- Gaming -------------------------------------------------------

        // GameDVR + Xbox deep block.
        list.Add(new ActionTweak(_store, "gamedvr",
            "GameDVR и Xbox",
            "Глубоко отключает GameDVR, AppCaptureEnabled и службы Xbox.",
            TweakCategory.Gaming,
            apply: async s =>
            {
                var ok = TweakOps.CaptureAndSet(s, HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
                ok &= TweakOps.CaptureAndSet(s, HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord);
                ok &= TweakOps.CaptureAndSet(s, HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, RegistryValueKind.DWord);
                // Disable Xbox helper services (Start=4). Reverting restores prior Start.
                foreach (var svc in new[] { "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc" })
                    ok &= TweakOps.CaptureAndSet(s, HKLM, $@"SYSTEM\CurrentControlSet\Services\{svc}", "Start", 4, RegistryValueKind.DWord);
                return await Task.FromResult(ok);
            },
            revert: TweakOps.RestoreAll));

        // Game Mode on.
        list.Add(new RegistryTweak(_store, "gamemode",
            "Игровой режим",
            "Включает Game Mode.",
            TweakCategory.Gaming, new[]
            {
                new RegistryOp(HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, RegistryValueKind.DWord),
                new RegistryOp(HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, RegistryValueKind.DWord),
            }));

        // Mouse acceleration off.
        list.Add(new RegistryTweak(_store, "mouseaccel",
            "Акселерация мыши",
            "Отключает ускорение указателя (MouseSpeed / MouseThreshold1/2 = 0).",
            TweakCategory.Gaming, new[]
            {
                new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String),
                new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String),
                new RegistryOp(HKCU, @"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String),
            }));

        // Extra guide-compatibility tweaks ("Легендарная установка", "Лучшая настройка").
        list.Add(new RegistryTweak(_store, "guidepack",
            "Совместимость с гайдами",
            "Дополнительные настройки: приоритет переднего плана и мгновенный запуск автозагрузки.",
            TweakCategory.System, new[]
            {
                // Favor foreground app (0x26 = short, variable, high foreground boost).
                new RegistryOp(HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26, RegistryValueKind.DWord),
                new RegistryOp(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, RegistryValueKind.DWord),
            }));

        // ---- Applications -------------------------------------------------

        // Yandex blocking (section 3) is exposed as a normal reversible tweak.
        list.Add(_yandex);

        return list;
    }

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
            var enumOptions = new EnumerationOptions
            {
                Timeout = TimeSpan.FromSeconds(15), ReturnImmediately = true, Rewindable = false,
            };
            using var searcher = new ManagementObjectSearcher(scope, query, enumOptions);
            using var results = searcher.Get();

            var any = false;
            foreach (ManagementObject vol in results)
            {
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
            }
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

    private async Task<bool> ApplyPowerPlan(TweakState s)
    {
        // Pre-check: remember the currently active scheme so revert can restore it.
        var current = await ProcessRunner.RunAsync("powercfg.exe", "/getactivescheme");
        var prevGuid = ExtractGuid(current.StdOut);
        if (prevGuid != null) s.Notes["prevScheme"] = prevGuid;

        // Duplicate the Ultimate Performance scheme (idempotent enough — a second
        // copy is harmless) and activate it.
        var dup = await ProcessRunner.RunAsync("powercfg.exe", $"-duplicatescheme {UltimateGuid}");
        var newGuid = ExtractGuid(dup.StdOut) ?? UltimateGuid;
        s.Notes["appliedScheme"] = newGuid;

        var set = await ProcessRunner.RunAsync("powercfg.exe", $"/setactive {newGuid}");
        return set.Success;
    }

    private async Task<bool> RevertPowerPlan(TweakState s)
    {
        if (s.Notes.TryGetValue("prevScheme", out var prev) && !string.IsNullOrWhiteSpace(prev))
        {
            var set = await ProcessRunner.RunAsync("powercfg.exe", $"/setactive {prev}");
            return set.Success;
        }
        return true; // nothing captured, nothing to restore
    }

    private static string? ExtractGuid(string text)
    {
        // powercfg prints "Power Scheme GUID: xxxxxxxx-xxxx-... (Name)".
        var m = System.Text.RegularExpressions.Regex.Match(text,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value : null;
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
            var snap = RegistryHelper.Capture(HKCU, subKey, name);
            s.Saved.Add(TweakStateStore.ToSaved(HKCU, subKey, name, snap));

            var ok = RegistryHelper.SetValue(HKCU, subKey, name, 50, RegistryValueKind.DWord);
            if (ok)
                Logger.Log("RawMouseThrottleDuration", "APPLIED",
                    $@"HKCU\{subKey}\{name}=50 (prior: {(snap.Existed ? snap.Value : "absent")})");
            else
                Logger.Log("RawMouseThrottleDuration", "FAILED",
                    $@"SetValue returned false for HKCU\{subKey}\{name}");
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
    /// flags in every device's "Device Parameters" sub-key. Each changed value is
    /// captured for exact rollback.
    /// </summary>
    private Task<bool> ApplyUsbPower(TweakState s) => Task.Run(() =>
    {
        const string root = @"SYSTEM\CurrentControlSet\Enum\USB";
        // Flags that, when set to 0, keep a USB device powered.
        string[] flags =
        {
            "EnhancedPowerManagementEnabled",
            "AllowIdleIrpInD3",
            "DeviceSelectiveSuspended",
            "SelectiveSuspendEnabled",
            "SelectiveSuspendOn",
        };

        var touched = 0;
        foreach (var device in RegistryHelper.SubKeyNames(HKLM, root))
        {
            foreach (var instance in RegistryHelper.SubKeyNames(HKLM, $@"{root}\{device}"))
            {
                var paramPath = $@"{root}\{device}\{instance}\Device Parameters";
                foreach (var flag in flags)
                {
                    var snap = RegistryHelper.Capture(HKLM, paramPath, flag);
                    // Only touch flags that already exist, to avoid polluting the tree.
                    if (!snap.Existed) continue;
                    s.Saved.Add(TweakStateStore.ToSaved(HKLM, paramPath, flag, snap));
                    if (RegistryHelper.SetValue(HKLM, paramPath, flag, 0, RegistryValueKind.DWord))
                        touched++;
                }
            }
        }

        // Also disable global USB selective suspend via the service key.
        TweakOps.CaptureAndSet(s, HKLM,
            @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1, RegistryValueKind.DWord);

        Logger.Log("USB power", "APPLIED", $"{touched} device flag(s) cleared");
        return true;
    });
}
