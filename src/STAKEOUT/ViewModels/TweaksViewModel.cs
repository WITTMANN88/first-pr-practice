using System.Collections.ObjectModel;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Mvvm;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One tweak row: wraps an <see cref="ITweak"/> and drives its toggle.</summary>
public sealed class TweakItemViewModel : ViewModelBase
{
    private readonly ITweak _tweak;
    private readonly INotificationService _notify;
    private readonly IDialogService _dialogs;
    private readonly Action<TweakItemViewModel> _onChanged;
    private bool _busy;
    private bool _failed;
    private bool _isOn;
    private bool _suppress; // stops apply/revert firing during programmatic sync
    private TweakStatus _status;
    /// <summary>Bumped by every user toggle, so a scan started earlier cannot overwrite it.</summary>
    private int _userVersion;

    /// <param name="tweak">The tweak this row drives.</param>
    /// <param name="notify">Toasts.</param>
    /// <param name="dialogs">Confirmation of destructive tweaks.</param>
    /// <param name="onChanged">Called after the user's apply or revert succeeded.</param>
    public TweakItemViewModel(ITweak tweak, INotificationService notify, IDialogService dialogs, Action<TweakItemViewModel> onChanged)
    {
        _tweak = tweak;
        _notify = notify;
        _dialogs = dialogs;
        _onChanged = onChanged;
        // Until the first scan (TweaksViewModel.RefreshStatesAsync) only the rollback record is known.
        _status = TweakStatus.Resolve(null, tweak.HasRollbackRecord);
        _isOn = _status.IsOn;
    }

    public string Title => _tweak.Title;

    /// <summary>Screen readers name the row by this, not by the type name.</summary>
    public override string ToString() => Title;
    public string Description => _tweak.Description;
    public bool IsDestructive => _tweak.IsDestructive;
    public bool RequiresRestart => _tweak.RequiresRestart;
    public bool RequiresExplorerRestart => _tweak.RequiresExplorerRestart;

    /// <summary>On in the system, but not applied by STAKEOUT: shown as on, cannot be undone here.</summary>
    public bool AppliedOutside => _status.AppliedOutside;

    /// <summary>
    /// Main text of the [?] tooltip, in plain words: what the tweak does, its
    /// pros and cons, when it takes effect and how to undo it.
    /// </summary>
    public string HelpText
    {
        get
        {
            var parts = new List<string> { _tweak.ExtendedDescription };
            if (RequiresRestart) parts.Add(Strings.Tweaks_HelpRestart);
            if (RequiresExplorerRestart) parts.Add(Strings.Tweaks_HelpExplorer);
            parts.Add(AppliedOutside ? Strings.Tweaks_HelpOutside : Strings.Tweaks_HelpRevert);
            return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    /// <summary>Registry values / commands, shown muted at the bottom of the tooltip ("" when none).</summary>
    public string HelpTechnical => _tweak.Details;

    public bool HasHelpTechnical => !string.IsNullOrWhiteSpace(_tweak.Details);

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>The toggle is disabled while an apply/revert for this tweak runs.</summary>
    public bool CanToggle => !Busy;

    /// <summary>Raised (false → true) when an apply/revert fails: the row shakes.</summary>
    public bool Failed
    {
        get => _failed;
        private set => SetProperty(ref _failed, value);
    }

    /// <summary>Bound to the toggle. Applying/reverting happens on change.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            if (Busy && !_suppress)
            {
                // A second click while the first change still runs would start an
                // overlapping apply/revert: refuse it and let the toggle snap back.
                OnPropertyChanged();
                return;
            }
            _isOn = value;
            OnPropertyChanged();
            if (_suppress) return;
            _userVersion++;
            _ = ToggleAsync(value);
        }
    }

    private static string F(string format, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);

    private async Task ToggleAsync(bool turnOn)
    {
        // Set outside STAKEOUT: there are no captured originals, so "off" has
        // nothing to restore. Say so and keep the toggle truthful (on).
        if (!turnOn && !_tweak.HasRollbackRecord)
        {
            _notify.Warning(F(Strings.Tweaks_RevertUnavailable, Title));
            Logger.Log(_tweak.Id, "REVERT-UNAVAILABLE", "set outside STAKEOUT; no rollback record");
            SetSilently(true);
            return;
        }

        // Confirm destructive actions before applying (BitLocker, MPO, UAC).
        if (turnOn && IsDestructive &&
            !_dialogs.Confirm(Strings.Common_ConfirmTitle, F(Strings.Tweaks_ConfirmDestructive, Title, Description)))
        {
            SetSilently(false);
            return;
        }

        Busy = true;
        Failed = false;
        try
        {
            var success = turnOn ? await _tweak.ApplyAsync() : await _tweak.RevertAsync();
            if (success)
            {
                SetStatus(TweakStatus.Resolve(turnOn, _tweak.HasRollbackRecord));
                _notify.Success(F(turnOn ? Strings.Tweaks_Enabled : Strings.Tweaks_Disabled, Title));
                _onChanged(this);
            }
            else
            {
                _notify.Error(F(turnOn ? Strings.Tweaks_ApplyFailed : Strings.Tweaks_RevertFailed, Title));
                SetStatus(TweakStatus.Resolve(await Task.Run(ReadSystemState), _tweak.HasRollbackRecord)); // reflect the real state
                Failed = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(_tweak.Id, ex);
            _notify.Error(F(Strings.Tweaks_Error, Title));
            SetStatus(TweakStatus.Resolve(await Task.Run(ReadSystemState), _tweak.HasRollbackRecord));
            Failed = true;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Set the toggle without triggering apply/revert.</summary>
    private void SetSilently(bool value)
    {
        _suppress = true;
        IsOn = value;
        _suppress = false;
    }

    /// <summary>Counts user toggles: a scan compares it before and after reading to drop stale results.</summary>
    internal int UserVersion => _userVersion;

    /// <summary>The system's answer for this tweak (I/O: call off the UI thread; never throws).</summary>
    internal bool? ReadSystemState() => _tweak.ReadCurrentState();

    /// <summary>
    /// Show what a scan found. Moves the toggle silently: no apply, no revert,
    /// no notification, no log line. Skipped while this tweak's own change runs.
    /// </summary>
    internal void ApplySystemState(bool? detected)
    {
        if (Busy) return;
        SetStatus(TweakStatus.Resolve(detected, _tweak.HasRollbackRecord));
    }

    private void SetStatus(TweakStatus status)
    {
        var outsideChanged = status.AppliedOutside != _status.AppliedOutside;
        _status = status;
        SetSilently(status.IsOn);
        if (!outsideChanged) return;
        OnPropertyChanged(nameof(AppliedOutside));
        OnPropertyChanged(nameof(HelpText));
    }
}

/// <summary>A category header plus its tweak rows.</summary>
public sealed class TweakGroup
{
    public string Header { get; init; } = "";
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();

    /// <summary>Screen readers name the group by this, not by the type name.</summary>
    public override string ToString() => Header;
}

/// <summary>The Tweaks page: grouped tweaks, explorer-restart pulse.</summary>
public sealed class TweaksViewModel : ViewModelBase
{
    private readonly INotificationService _notify;
    private bool _pendingExplorerRestart;
    private bool _scanning;
    private bool _rescanRequested;
    private string? _lastScanSummary;

    public TweaksViewModel(TweakService service, INotificationService notify, IDialogService dialogs)
    {
        _notify = notify;

        foreach (var group in service.Tweaks.GroupBy(t => t.Category))
        {
            var g = new TweakGroup { Header = CategoryLabel(group.Key) };
            foreach (var t in group)
                g.Items.Add(new TweakItemViewModel(t, notify, dialogs, OnItemChanged));
            Groups.Add(g);
        }

        RestartExplorerCommand = new AsyncRelayCommand(async _ =>
        {
            await SystemActions.RestartExplorerAsync();
            PendingExplorerRestart = false;
            _notify.Success(Strings.Tweaks_ExplorerRestarted);
        });
    }

    public ObservableCollection<TweakGroup> Groups { get; } = new();
    public AsyncRelayCommand RestartExplorerCommand { get; }

    public bool PendingExplorerRestart
    {
        get => _pendingExplorerRestart;
        private set => SetProperty(ref _pendingExplorerRestart, value);
    }

    /// <summary>
    /// Read what the system actually has for every tweak (off the UI thread) and
    /// move the toggles to match, silently: nothing is applied or reverted and
    /// no per-tweak log lines are written. Runs at startup, on every visit to
    /// the page and after "Отменить всё"; a call during a scan queues one more
    /// pass. A toggle the user changed while the scan ran keeps the user's value.
    /// </summary>
    public async Task RefreshStatesAsync()
    {
        if (_scanning)
        {
            _rescanRequested = true;
            return;
        }
        _scanning = true;
        try
        {
            do
            {
                _rescanRequested = false;
                await ScanOnceAsync();
            }
            while (_rescanRequested);
        }
        catch (Exception ex)
        {
            // Fire-and-forget from navigation: never let a failure go unobserved.
            Logger.LogError("Tweaks.Scan", ex);
        }
        finally
        {
            _scanning = false;
        }
    }

    private async Task ScanOnceAsync()
    {
        var items = Groups.SelectMany(g => g.Items).ToList();
        var versions = items.Select(i => i.UserVersion).ToArray();

        // Registry reads, one WMI query (BitLocker): bounded, so a hung provider
        // leaves the toggles as they are instead of blocking the page.
        var states = await TimeoutGuard.Await(
            Task.Run<bool?[]?>(() => items.Select(i => i.ReadSystemState()).ToArray()),
            TimeSpan.FromSeconds(30), null, "Tweaks.Scan");
        if (states is null) return;

        for (var i = 0; i < items.Count; i++)
            if (items[i].UserVersion == versions[i]) items[i].ApplySystemState(states[i]);

        // One line per scan, and only when the picture changed: repeated visits
        // to the page do not fill the log.
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{items.Count(i => i.IsOn)}/{items.Count} on ({items.Count(i => i.AppliedOutside)} set outside STAKEOUT, {states.Count(s => s is null)} unreadable)");
        if (summary == _lastScanSummary) return;
        _lastScanSummary = summary;
        Logger.Log("Tweaks.Scan", "OK", summary);
    }

    /// <summary>
    /// A change the user just made to a tweak that shows only after Explorer
    /// restarts makes the restart button pulse. Settings found already in place
    /// by a scan do not: nothing changed, so there is nothing to restart for.
    /// </summary>
    private void OnItemChanged(TweakItemViewModel item)
    {
        if (item.RequiresExplorerRestart) PendingExplorerRestart = true;
    }

    /// <summary>Design-time entry point (Design/DesignData): show the pulsing state.</summary>
    internal void ShowExplorerRestartPending() => PendingExplorerRestart = true;

    private static string CategoryLabel(TweakCategory c) => c switch
    {
        TweakCategory.Privacy => Strings.TweakCategory_Privacy,
        TweakCategory.Security => Strings.TweakCategory_Security,
        TweakCategory.Performance => Strings.TweakCategory_Performance,
        TweakCategory.Gaming => Strings.TweakCategory_Gaming,
        TweakCategory.System => Strings.TweakCategory_System,
        TweakCategory.Interface => Strings.TweakCategory_Interface,
        _ => c.ToString(),
    };
}
