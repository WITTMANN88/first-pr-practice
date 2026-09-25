using Stakeout.Core;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>
/// Installs software two ways:
///   * Winget for the mainstream apps (Chrome, 7-Zip, Discord, Steam). Launched
///     with --interactive so the real installer UI appears and the user can pick
///     the install path (no silent mode, per the spec).
///   * Direct download for ISLC and MakuTweaker: the file is fetched to Downloads
///     with a progress bar, then started visibly so the user drives setup.
///
/// NOTE: the direct download URLs are configuration. Fill DownloadUrl with the
/// exact links you were given; until then those items report "ссылка не настроена".
/// </summary>
public sealed class SoftwareInstallService
{
    private readonly DownloadService _download;
    public SoftwareInstallService(DownloadService download) => _download = download;

    /// <summary>Catalog of installable programs shown on the software page.</summary>
    public IReadOnlyList<SoftwareItem> Catalog { get; } = new List<SoftwareItem>
    {
        new() { Key = "chrome",   DisplayName = "Google Chrome", Method = InstallMethod.Winget, WingetId = "Google.Chrome" },
        new() { Key = "7zip",     DisplayName = "7-Zip",         Method = InstallMethod.Winget, WingetId = "7zip.7zip" },
        new() { Key = "discord",  DisplayName = "Discord",       Method = InstallMethod.Winget, WingetId = "Discord.Discord" },
        new() { Key = "steam",    DisplayName = "Steam",         Method = InstallMethod.Winget, WingetId = "Valve.Steam" },
        // Direct downloads.
        new() { Key = "islc",     DisplayName = "ISLC (Intelligent Standby List Cleaner)",
                Method = InstallMethod.DirectDownload, FileName = "ISLC_v1.0.4.7_setup.exe",
                DownloadUrl = "https://download.wagnardsoft.com/ISLC/ISLC%20v1.0.4.7_setup.exe" },
        new() { Key = "maku",     DisplayName = "MakuTweaker",
                Method = InstallMethod.DirectDownload, FileName = "MakuTweaker.5.7.3.Setup.exe",
                DownloadUrl = "https://github.com/MarkAdderly/MakuTweaker/releases/download/release57/MakuTweaker.5.7.3.Setup.exe" },
    };

    /// <summary>
    /// Install one item. <paramref name="stage"/> reports the morphing status text
    /// ("Скачивание..." -> "Установка..." -> "Готово"); <paramref name="progress"/>
    /// drives the bar for direct downloads.
    /// </summary>
    public async Task<bool> InstallAsync(SoftwareItem item,
        IProgress<InstallStage> stage, IProgress<double> progress, CancellationToken ct = default)
    {
        try
        {
            if (item.Method == InstallMethod.Winget)
                return await InstallViaWinget(item, stage, ct);

            return await InstallViaDownload(item, stage, progress, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError("Install " + item.Key, ex);
            stage.Report(InstallStage.Failed);
            return false;
        }
    }

    private static async Task<bool> InstallViaWinget(SoftwareItem item,
        IProgress<InstallStage> stage, CancellationToken ct)
    {
        stage.Report(InstallStage.Installing);
        // --interactive shows the installer window so the user can choose the path.
        var args = $"install --id {item.WingetId} -e --interactive " +
                   "--accept-package-agreements --accept-source-agreements";
        var result = await ProcessRunner.RunAsync("winget.exe", args, ct);

        var ok = result.Success;
        stage.Report(ok ? InstallStage.Done : InstallStage.Failed);
        Logger.Log("Winget install", ok ? "OK" : "ERROR", $"{item.WingetId} (exit {result.ExitCode})");
        return ok;
    }

    private async Task<bool> InstallViaDownload(SoftwareItem item,
        IProgress<InstallStage> stage, IProgress<double> progress, CancellationToken ct)
    {
        if (item.NeedsUrlConfig)
        {
            Logger.Log("Direct install", "SKIP", $"{item.Key}: URL not configured");
            stage.Report(InstallStage.Failed);
            return false;
        }

        stage.Report(InstallStage.Downloading);
        var path = await _download.DownloadAsync(item.DownloadUrl!, item.FileName!, progress, ct);
        if (path == null)
        {
            stage.Report(InstallStage.Failed);
            return false;
        }

        // Launch the installer visibly so the user picks the path themselves.
        stage.Report(InstallStage.Installing);
        ProcessRunner.LaunchVisible(path);
        stage.Report(InstallStage.Done);
        return true;
    }
}
