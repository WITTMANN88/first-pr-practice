namespace Stakeout.Models;

public enum InstallMethod { Winget, DirectDownload }

/// <summary>Status shown on a software card, morphed during install.</summary>
public enum InstallStage { Idle, Downloading, Extracting, Installing, Done, Failed, Background, Blocked }

/// <summary>How an install attempt ended (drives the toast).</summary>
public enum InstallOutcome
{
    Started,
    Failed,
    Cancelled,
    /// <summary>winget was detached on cancel: it finishes on its own, never killed mid-install.</summary>
    ContinuesInBackground,
    /// <summary>The download's SHA-256 differs from the pinned reference; the file was deleted.</summary>
    IntegrityFailure,
    /// <summary>No reference SHA-256 in the catalog: direct installs are refused.</summary>
    HashNotConfigured,
}

/// <summary>One installable program shown as a card on the software page.</summary>
public sealed class SoftwareItem
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public InstallMethod Method { get; set; }

    /// <summary>winget package id, e.g. "Google.Chrome".</summary>
    public string? WingetId { get; set; }

    /// <summary>Direct installer URL (for ISLC / MakuTweaker). HTTPS only.</summary>
    public Uri? DownloadUrl { get; set; }
    public string? FileName { get; set; }

    /// <summary>
    /// Reference SHA-256 of the installer (64 hex digits, as printed by
    /// Get-FileHash). A download that does not match is deleted and not run;
    /// without a usable value (<see cref="Stakeout.Core.Sha256Hash.Placeholder"/>)
    /// the install is refused before downloading.
    /// </summary>
    public string? Sha256 { get; set; }

    /// <summary>True when a direct URL still needs to be configured by the user.</summary>
    public bool NeedsUrlConfig =>
        Method == InstallMethod.DirectDownload &&
        (DownloadUrl is null || DownloadUrl.OriginalString.Contains("REPLACE_WITH", StringComparison.Ordinal));
}
