using System.Collections.ObjectModel;
using System.Globalization;
using Stakeout.Core;
using Stakeout.Localization;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One tweak row: wraps an <see cref="ITweak"/> and drives its toggle.</summary>
public sealed class TweakItemViewModel : ViewModelBase
{
    private readonly ITweak _tweak;
    private readonly INotificationService _notify;
    private readonly IDialogService _dialogs;
    private readonly Action _onStateChanged;
    private bool _busy;
    private bool _failed;
    private bool _isOn;
    private bool _suppress; // stops apply/revert firing during programmatic sync

    public TweakItemViewModel(ITweak tweak, INotificationService notify, IDialogService dialogs, Action onStateChanged)
    {
        _tweak = tweak;
        _notify = notify;
        _dialogs = dialogs;
        _onStateChanged = onStateChanged;
        _isOn = tweak.IsApplied;
    }

    public string Title => _tweak.Title;
    public string Description => _tweak.Description;
    public bool IsDestructive => _tweak.IsDestructive;
    public bool RequiresRestart => _tweak.RequiresRestart;
    public bool RequiresExplorerRestart => _tweak.RequiresExplorerRestart;

    /// <summary>
    /// The [?] tooltip: what exactly changes (the description is already on the
    /// row), when it takes effect and how to undo it.
    /// </summary>
    public string HelpText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_tweak.Details)) parts.Add(Strings.Tweaks_HelpChanges + "\n" + _tweak.Details);
            if (RequiresRestart) parts.Add(Strings.Tweaks_HelpRestart);
            if (RequiresExplorerRestart) parts.Add(Strings.Tweaks_HelpExplorer);
            parts.Add(Strings.Tweaks_HelpRevert);
            return string.Join("\n\n", parts);
        }
    }

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
            if (!_suppress) _ = ToggleAsync(value);
        }
    }

    private static string F(string format, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);

    private async Task ToggleAsync(bool turnOn)
    {
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
                _notify.Success(F(turnOn ? Strings.Tweaks_Enabled : Strings.Tweaks_Disabled, Title));
            }
            else
            {
                _notify.Error(F(turnOn ? Strings.Tweaks_ApplyFailed : Strings.Tweaks_RevertFailed, Title));
                SetSilently(_tweak.IsApplied); // reflect the real state
                Failed = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(_tweak.Id, ex);
            _notify.Error(F(Strings.Tweaks_Error, Title));
            SetSilently(_tweak.IsApplied);
            Failed = true;
        }
        finally
        {
            Busy = false;
            _onStateChanged();
        }
    }

    /// <summary>Set the toggle without triggering apply/revert.</summary>
    private void SetSilently(bool value)
    {
        _suppress = true;
        IsOn = value;
        _suppress = false;
    }

    /// <summary>Re-read applied state from the model without firing apply/revert.</summary>
    public void SyncFromModel() => SetSilently(_tweak.IsApplied);
}

/// <summary>A category header plus its tweak rows.</summary>
public sealed class TweakGroup
{
    public string Header { get; init; } = "";
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();
}

/// <summary>The Tweaks page: grouped tweaks, explorer-restart pulse.</summary>
public sealed class TweaksViewModel : ViewModelBase
{
    private readonly INotificationService _notify;
    private bool _pendingExplorerRestart;

    public TweaksViewModel(TweakService service, INotificationService notify, IDialogService dialogs)
    {
        _notify = notify;

        foreach (var group in service.Tweaks.GroupBy(t => t.Category))
        {
            var g = new TweakGroup { Header = CategoryLabel(group.Key) };
            foreach (var t in group)
                g.Items.Add(new TweakItemViewModel(t, notify, dialogs, RecomputePending));
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

    /// <summary>Refresh every toggle from the store (used after "Отменить всё").</summary>
    public void SyncAll()
    {
        foreach (var item in Groups.SelectMany(g => g.Items))
            item.SyncFromModel();
        RecomputePending();
    }

    private void RecomputePending()
    {
        // Pulse the restart button if any explorer-visual tweak is currently on.
        PendingExplorerRestart = Groups
            .SelectMany(g => g.Items)
            .Any(i => i.RequiresExplorerRestart && i.IsOn);
    }

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
