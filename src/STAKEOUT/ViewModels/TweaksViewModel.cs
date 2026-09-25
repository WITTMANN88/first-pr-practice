using System.Collections.ObjectModel;
using System.Windows;
using Stakeout.Core;
using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.ViewModels;

/// <summary>One tweak row: wraps an <see cref="ITweak"/> and drives its toggle.</summary>
public sealed class TweakItemViewModel : ViewModelBase
{
    private readonly ITweak _tweak;
    private readonly ToastService _toast;
    private readonly Action _onStateChanged;
    private bool _busy;
    private bool _isOn;
    private bool _suppress; // stops apply/revert firing during initial sync

    public TweakItemViewModel(ITweak tweak, ToastService toast, Action onStateChanged)
    {
        _tweak = tweak;
        _toast = toast;
        _onStateChanged = onStateChanged;
        _suppress = true;
        _isOn = tweak.IsApplied;
        _suppress = false;
    }

    public string Title => _tweak.Title;
    public string Description => _tweak.Description;
    public bool IsDestructive => _tweak.IsDestructive;
    public bool RequiresRestart => _tweak.RequiresRestart;
    public bool RequiresExplorerRestart => _tweak.RequiresExplorerRestart;

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    /// <summary>Bound to the toggle. Applying/reverting happens on change.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            OnPropertyChanged();
            if (!_suppress) _ = ToggleAsync(value);
        }
    }

    private async Task ToggleAsync(bool turnOn)
    {
        // Confirm destructive actions before applying (BitLocker, MPO, UAC).
        if (turnOn && IsDestructive)
        {
            var ok = MessageBox.Show(
                $"«{Title}» — потенциально опасное действие.\n\n{Description}\n\nПродолжить?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes;
            if (!ok)
            {
                Revert(); // roll the toggle back visually
                return;
            }
        }

        Busy = true;
        try
        {
            var success = turnOn ? await _tweak.ApplyAsync() : await _tweak.RevertAsync();
            if (success)
            {
                _toast.Success($"{Title}: {(turnOn ? "включено" : "отключено")}");
            }
            else
            {
                _toast.Error($"{Title}: не удалось {(turnOn ? "применить" : "откатить")}");
                Revert();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(Title, ex);
            _toast.Error($"{Title}: ошибка");
            Revert();
        }
        finally
        {
            Busy = false;
            _onStateChanged();
        }
    }

    /// <summary>Flip the toggle back without re-triggering apply/revert.</summary>
    private void Revert()
    {
        _suppress = true;
        IsOn = !_isOn;
        _suppress = false;
    }

    /// <summary>Re-read applied state from the model without firing apply/revert.</summary>
    public void SyncFromModel()
    {
        _suppress = true;
        IsOn = _tweak.IsApplied;
        _suppress = false;
    }
}

/// <summary>A category header plus its tweak rows.</summary>
public sealed class TweakGroup
{
    public string Header { get; init; } = "";
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();
}

/// <summary>The Tweaks page: grouped tweaks, explorer-restart pulse, revert-all.</summary>
public sealed class TweaksViewModel : ViewModelBase
{
    private readonly TweakService _service;
    private readonly ToastService _toast;
    private bool _pendingExplorerRestart;

    public ObservableCollection<TweakGroup> Groups { get; } = new();
    public RelayCommand RestartExplorerCommand { get; }

    public bool PendingExplorerRestart
    {
        get => _pendingExplorerRestart;
        private set => SetProperty(ref _pendingExplorerRestart, value);
    }

    public TweaksViewModel(TweakService service, ToastService toast)
    {
        _service = service;
        _toast = toast;

        foreach (var group in service.Tweaks.GroupBy(t => t.Category))
        {
            var g = new TweakGroup { Header = CategoryLabel(group.Key) };
            foreach (var t in group)
                g.Items.Add(new TweakItemViewModel(t, toast, RecomputePending));
            Groups.Add(g);
        }

        RestartExplorerCommand = new RelayCommand(async () =>
        {
            await SystemActions.RestartExplorerAsync();
            PendingExplorerRestart = false;
            _toast.Success("Проводник перезапущен");
        });
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
        TweakCategory.Privacy => "Приватность и телеметрия",
        TweakCategory.Security => "Безопасность",
        TweakCategory.Performance => "Производительность",
        TweakCategory.Gaming => "Игры",
        TweakCategory.System => "Система",
        TweakCategory.Interface => "Интерфейс",
        _ => c.ToString()
    };
}
