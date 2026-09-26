namespace Stakeout.Models;

public enum InstallMethod { Winget, DirectDownload }

/// <summary>Status shown on a software card, morphed during install.</summary>
public enum InstallStage { Idle, Downloading, Extracting, Installing, Done, Failed }

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

    /// <summary>True when a direct URL still needs to be configured by the user.</summary>
    public bool NeedsUrlConfig =>
        Method == InstallMethod.DirectDownload &&
        (DownloadUrl is null || DownloadUrl.OriginalString.Contains("REPLACE_WITH", StringComparison.Ordinal));
}
