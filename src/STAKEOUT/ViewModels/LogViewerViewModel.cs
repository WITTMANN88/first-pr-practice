using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;

namespace Stakeout.ViewModels;

/// <summary>
/// In-app log viewer (modal overlay). Each open re-reads the encrypted log from
/// %TEMP%\STAKEOUT and decrypts it on a background thread, so the view always
/// reflects the current session and the UI thread never blocks on I/O or AES.
/// </summary>
public sealed class LogViewerViewModel : ViewModelBase
{
    /// <summary>
    /// Render cap. The list is virtualized, but a bounded collection keeps
    /// memory and bind time predictable on very long sessions. Copy always
    /// takes the full log.
    /// </summary>
    private const int MaxDisplayLines = 5000;

    private readonly INotificationService _notify;
    private IReadOnlyList<string> _raw = Array.Empty<string>();
    private IReadOnlyList<LogEntry> _entries = Array.Empty<LogEntry>();
    private string? _pathOverride;
    private bool _isOpen;
    private bool _isLoading;
    private string _summary = "";

    public LogViewerViewModel(INotificationService notify)
    {
        _notify = notify;
        OpenCommand = new AsyncRelayCommand(_ => OpenAsync());
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());
        CopyCommand = new AsyncRelayCommand(_ => CopyAsync(), _ => !IsLoading && _raw.Count > 0);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CopyCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Drives the overlay's open/close animation in the shell.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Replaced wholesale per load: one collection notification, not N adds.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get => _entries;
        private set
        {
            if (SetProperty(ref _entries, value)) OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => !IsLoading && Entries.Count == 0;
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string LogPath => _pathOverride
        ?? (string.IsNullOrEmpty(Logger.LogPath) ? Strings.Logs_Unavailable : Logger.LogPath);

    private async Task OpenAsync()
    {
        IsOpen = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            // Decrypt off the UI thread; cap the wait so a stuck disk never
            // leaves the modal spinning forever.
            var lines = await TimeoutGuard.Await<IReadOnlyList<string>>(
                Task.Run<IReadOnlyList<string>>(() => Logger.ReadDecrypted().ToList()),
                TimeSpan.FromSeconds(15), Array.Empty<string>(), "LogViewer.Load");

            // Parse only what will be shown (the tail), also off the UI thread.
            var parsed = await Task.Run(() => ParseTail(lines));
            Show(lines, parsed);
        }
        finally
        {
            IsLoading = false;
            CopyCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Show already-decrypted lines synchronously. Design-time entry point
    /// (Design/DesignData): no disk, no decryption, no background threads.
    /// </summary>
    internal void ShowLines(IReadOnlyList<string> lines, string? logPath = null, bool open = false)
    {
        _pathOverride = logPath;
        OnPropertyChanged(nameof(LogPath));
        Show(lines, ParseTail(lines));
        IsOpen = open;
        CopyCommand.NotifyCanExecuteChanged();
    }

    private static List<LogEntry> ParseTail(IReadOnlyList<string> lines)
        => lines.Skip(Math.Max(0, lines.Count - MaxDisplayLines)).Select(LogEntry.Parse).ToList();

    private void Show(IReadOnlyList<string> lines, IReadOnlyList<LogEntry> entries)
    {
        _raw = lines;
        Entries = entries;
        Summary = lines.Count > MaxDisplayLines
            ? string.Format(CultureInfo.CurrentCulture, Strings.Logs_LineCountTruncated, lines.Count, MaxDisplayLines)
            : string.Format(CultureInfo.CurrentCulture, Strings.Logs_LineCount, lines.Count);
    }

    /// <summary>
    /// Copy the full decrypted log. The clipboard is a shared OS resource and
    /// throws (CLIPBRD_E_CANT_OPEN) while another process holds it, so retry
    /// briefly before giving up.
    /// </summary>
    private async Task CopyAsync()
    {
        var text = string.Join(Environment.NewLine, _raw);
        const int attempts = 5;
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                Clipboard.SetText(text);
                _notify.Success(string.Format(CultureInfo.CurrentCulture, Strings.Logs_Copied, _raw.Count));
                Logger.Log("LogViewer", "OK", "copied to clipboard");
                return;
            }
            catch (ExternalException) when (i < attempts)
            {
                await Task.Delay(80);
            }
            catch (Exception ex)
            {
                Logger.LogError("LogViewer.Copy", ex);
                _notify.Error(Strings.Logs_CopyFailed);
                return;
            }
        }
    }
}
