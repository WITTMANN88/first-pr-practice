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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded",
        Justification = "The catalog is data: each entry pins one vendor release URL, reviewed with the code.")]
    public IReadOnlyList<SoftwareItem> Catalog { get; } = new List<SoftwareItem>
    {
        new() { Key = "chrome",   DisplayName = "Google Chrome", Method = InstallMethod.Winget, WingetId = "Google.Chrome" },
        new() { Key = "7zip",     DisplayName = "7-Zip",         Method = InstallMethod.Winget, WingetId = "7zip.7zip" },
        new() { Key = "discord",  DisplayName = "Discord",       Method = InstallMethod.Winget, WingetId = "Discord.Discord" },
        new() { Key = "steam",    DisplayName = "Steam",         Method = InstallMethod.Winget, WingetId = "Valve.Steam" },
        // Direct downloads, each pinned to a reference SHA-256 (supply-chain check).
        // TODO: UPDATE_HASH after the first manual download of each file
        // (PowerShell: Get-FileHash <file> -Algorithm SHA256). Until then these
        // installs are refused: without a reference nothing can be verified.
        new() { Key = "islc",     DisplayName = "ISLC (Intelligent Standby List Cleaner)",
                Method = InstallMethod.DirectDownload, FileName = "ISLC_v1.0.4.7_setup.exe",
                DownloadUrl = new Uri("https://download.wagnardsoft.com/ISLC/ISLC%20v1.0.4.7_setup.exe"),
                Sha256 = "TODO: UPDATE_HASH" },
        new() { Key = "maku",     DisplayName = "MakuTweaker",
                Method = InstallMethod.DirectDownload, FileName = "MakuTweaker.5.7.3.Setup.exe",
                DownloadUrl = new Uri("https://github.com/MarkAdderly/MakuTweaker/releases/download/release57/MakuTweaker.5.7.3.Setup.exe"),
                Sha256 = "TODO: UPDATE_HASH" },
    };

    /// <summary>
    /// Install one item. <paramref name="stage"/> reports the morphing status text
    /// ("Скачивание..." -> "Установка..." -> "Готово"); <paramref name="progress"/>
    /// drives the bar for direct downloads. Never throws.
    /// </summary>
    public async Task<InstallOutcome> InstallAsync(SoftwareItem item,
        IProgress<InstallStage> stage, IProgress<double> progress, CancellationToken ct = default)
    {
        try
        {
            return item.Method == InstallMethod.Winget
                ? await InstallViaWinget(item, stage, ct)
                : await InstallViaDownload(item, stage, progress, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError("Install " + item.Key, ex);
            stage.Report(InstallStage.Failed);
            return InstallOutcome.Failed;
        }
    }

    private static async Task<InstallOutcome> InstallViaWinget(SoftwareItem item,
        IProgress<InstallStage> stage, CancellationToken ct)
    {
        if (SystemTools.Winget is not { } winget)
        {
            Logger.Log("Winget install", "ERROR", "winget.exe not found (App Installer missing)");
            stage.Report(InstallStage.Failed);
            return InstallOutcome.Failed;
        }

        stage.Report(InstallStage.Installing);
        // --interactive shows the installer window so the user can choose the path.
        var args = $"install --id {item.WingetId} -e --interactive " +
                   "--accept-package-agreements --accept-source-agreements";

        // Cancel (or the 30-minute cap, as interactive installers wait for the
        // user) detaches instead of killing: an installer killed half-way can
        // leave a broken program behind. winget then finishes on its own and
        // its exit code is logged.
        var result = await ProcessRunner.RunAsync(winget, args,
            timeoutMs: 30 * 60_000, onAbandon: AbandonPolicy.Detach, ct: ct);

        if (result.Detached)
        {
            stage.Report(InstallStage.Background);
            Logger.Log("Winget install", "DETACHED", $"{item.WingetId}: {result.StdErr}");
            return InstallOutcome.ContinuesInBackground;
        }

        var ok = result.Success;
        stage.Report(ok ? InstallStage.Done : InstallStage.Failed);
        Logger.Log("Winget install", ok ? "OK" : "ERROR", $"{item.WingetId} (exit {result.ExitCode})");
        return ok ? InstallOutcome.Started : InstallOutcome.Failed;
    }

    private async Task<InstallOutcome> InstallViaDownload(SoftwareItem item,
        IProgress<InstallStage> stage, IProgress<double> progress, CancellationToken ct)
    {
        if (item.NeedsUrlConfig || item.FileName is null)
        {
            Logger.Log("Direct install", "SKIP", $"{item.Key}: URL not configured");
            stage.Report(InstallStage.Failed);
            return InstallOutcome.Failed;
        }

        stage.Report(InstallStage.Downloading);
        var download = await _download.DownloadAsync(item.DownloadUrl!, item.FileName, item.Sha256, progress, ct);
        switch (download.Status)
        {
            case DownloadStatus.Completed:
                // Verified: launch visibly so the user picks the path themselves.
                stage.Report(InstallStage.Installing);
                ProcessRunner.LaunchVisible(download.Path!);
                stage.Report(InstallStage.Done);
                return InstallOutcome.Started;
            case DownloadStatus.Cancelled:
                stage.Report(InstallStage.Idle);
                return InstallOutcome.Cancelled;
            case DownloadStatus.HashMismatch:
                stage.Report(InstallStage.Blocked);
                return InstallOutcome.IntegrityFailure;
            case DownloadStatus.HashNotConfigured:
                stage.Report(InstallStage.Blocked);
                return InstallOutcome.HashNotConfigured;
            default:
                stage.Report(InstallStage.Failed);
                return InstallOutcome.Failed;
        }
    }
}
