using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Services;
using Stakeout.ViewModels;

namespace Stakeout.Design;

/// <summary>
/// Pre-filled view models for the Visual Studio / Rider XAML designer:
/// <code>d:DataContext="{x:Static design:DesignData.SysInfo}"</code>
///
/// The d: namespace is mc:Ignorable, so the markup compiler drops those
/// attributes: nothing here is referenced from BAML or constructed at runtime.
///
/// These are the real view models (so the designer resolves the real binding
/// paths), filled through their internal design hooks and wired to design-safe
/// services: an in-memory tweak store, a registry that is never written, a
/// dialog that never opens, toasts that never expire. Nothing touches the disk,
/// the registry, WMI, sensors or PowerShell, and no timer is started.
/// Data comes from <see cref="DesignSamples"/> (unit-tested in STAKEOUT.Core).
/// </summary>
public static class DesignData
{
    // One graph shared by all properties, so the shell and its pages agree.
    private static readonly Lazy<Graph> Instance = new(() => new Graph());

    public static MainViewModel Main => Instance.Value.Shell;
    public static SysInfoViewModel SysInfo => Instance.Value.Shell.SysInfo;
    public static TweaksViewModel Tweaks => Instance.Value.Shell.Tweaks;
    public static UwpViewModel Uwp => Instance.Value.Shell.Uwp;
    public static SoftwareViewModel Software => Instance.Value.Shell.Software;
    public static LogViewerViewModel Logs => Instance.Value.Shell.Logs;

    private sealed class Graph
    {
        public Graph()
        {
            var notifications = new NotificationService(
                new InlineDispatcher(), NotificationService.DefaultLifetime, NotificationService.DefaultMaxVisible,
                (_, _) => new TaskCompletionSource().Task); // never completes: toasts stay on the canvas
            var dialogs = new NeverConfirm();

            // Tweaks: a few applied, including an explorer-visual one so the
            // "restart Explorer" button shows its pulsing state.
            var store = TweakStateStore.CreateInMemory();
            foreach (var id in new[] { "diagtrack", "advid", "animations", "mpo", "gamemode" })
                store.MarkApplied(id, new TweakState());
            var rollback = new RegistryRollback(new ReadOnlyRegistry());
            var tweakService = new TweakService(store, rollback, new YandexBlockService(store, rollback));
            var tweaks = new TweaksViewModel(tweakService, notifications, dialogs);
            tweaks.SyncAll();

            var sysInfo = new SysInfoViewModel(new SystemInfoService());
            FillSysInfo(sysInfo);

            var uwp = new UwpViewModel(new UwpService(), notifications);
            uwp.ShowApps(DesignSamples.UwpApps());
            foreach (var app in uwp.Apps.Where(a => a.Category == UwpCategory.Bloatware).Take(2))
                app.IsSelected = true;
            uwp.AddFreed(64L * 1024 * 1024 + 318L * 1024);

            var software = new SoftwareViewModel(new SoftwareInstallService(new DownloadService()), notifications);
            software.Items.FirstOrDefault(i => i.Item.Method != InstallMethod.Winget)?.ShowProgress(InstallStage.Downloading, 42);
            software.Items.FirstOrDefault(i => i.Item.Method == InstallMethod.Winget)?.ShowProgress(InstallStage.Installing, 0);

            var logs = new LogViewerViewModel(notifications);
            logs.ShowLines(DesignSamples.LogLines(),
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "STAKEOUT", "stakeout-20260926.log"), open: true);

            Shell = new MainViewModel(sysInfo, tweaks, uwp, software, logs, tweakService, notifications, notifications);

            // One toast of each visible kind, using the real message formats.
            notifications.Success(F(Strings.Software_Started, "7-Zip"));
            notifications.Warning(F(Strings.State_RecoveredFromBackup, "tweak-state.20260926T180211.4410032Z.json"));
            notifications.Error(F(Strings.Uwp_RemoveFailed, "XboxGameCallableUI"));
        }

        public MainViewModel Shell { get; }

        private static void FillSysInfo(SysInfoViewModel vm)
        {
            // History first; the snapshot then adds the newest reading.
            var temps = DesignSamples.CpuTemperatures();
            foreach (var t in temps.Take(temps.Count - 1)) vm.RecordTemperature(t);

            vm.ApplySnapshot(new SystemInfoModel
            {
                CpuName = "12th Gen Intel(R) Core(TM) i7-12700H",
                CpuTemperatureC = temps[^1],
                Motherboard = "Micro-Star International Co., Ltd. MS-17L2",
                Gpus = new[]
                {
                    new GpuInfo { Name = "NVIDIA GeForce RTX 3060 Laptop GPU", Kind = Strings.SysInfo_GpuDiscrete },
                    new GpuInfo { Name = "Intel(R) Iris(R) Xe Graphics", Kind = Strings.SysInfo_GpuIntegrated },
                },
                RamSummary = F(Strings.Unit_Gigabytes, 32) + " DDR4",
                DiskSummary = F(Strings.SysInfo_DiskTotal, F(Strings.Unit_Terabytes, 1.93)),
                WindowsVersion = F(Strings.SysInfo_WindowsBuild, "Windows 10 Pro 22H2", "19045.4894"),
            });
        }

        private static string F(string format, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, format, args);
    }

    /// <summary>Runs posted work immediately (the designer is single-threaded).</summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class NeverConfirm : IDialogService
    {
        public bool Confirm(string title, string message) => false;
    }

    /// <summary>Reads nothing, writes nothing: a design surface must never touch the real registry.</summary>
    private sealed class ReadOnlyRegistry : IRegistryAccess
    {
        public RegistryValueSnapshot Capture(RegHive hive, string subKey, string name) => RegistryValueSnapshot.Absent;
        public bool SetValue(RegHive hive, string subKey, string name, object value, RegValueKind kind) => false;
        public bool DeleteValue(RegHive hive, string subKey, string name) => false;
    }
}
