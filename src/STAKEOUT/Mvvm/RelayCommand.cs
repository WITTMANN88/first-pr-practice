using System.Windows.Input;
using Stakeout.Core;

namespace Stakeout.Mvvm;

/// <summary>Standard synchronous ICommand implementation for MVVM bindings.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private EventHandler? _canExecuteChanged;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    // Subscribers get both: WPF's global requery (on focus/input changes, held
    // weakly by CommandManager) and this command's own notification.
    public event EventHandler? CanExecuteChanged
    {
        add
        {
            _canExecuteChanged += value;
            CommandManager.RequerySuggested += value;
        }
        remove
        {
            _canExecuteChanged -= value;
            CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// Re-query this command now. Unlike CommandManager.InvalidateRequerySuggested
    /// (which re-evaluates every command in the app on a later dispatcher pass),
    /// this is synchronous and scoped to one command. Call on the UI thread.
    /// </summary>
    public void NotifyCanExecuteChanged() => _canExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Async ICommand. Guards against re-entrancy while the task is running so the
/// bound button cannot fire twice. All long-running work (PowerShell, downloads,
/// WMI) is dispatched through this to keep the UI responsive.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;
    private EventHandler? _canExecuteChanged;

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

    public bool CanExecute(object? parameter)
        => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        NotifyCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            Logger.LogError("AsyncCommand", ex);
        }
        finally
        {
            _isRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    // Subscribers get both: WPF's global requery (on focus/input changes, held
    // weakly by CommandManager) and this command's own notification.
    public event EventHandler? CanExecuteChanged
    {
        add
        {
            _canExecuteChanged += value;
            CommandManager.RequerySuggested += value;
        }
        remove
        {
            _canExecuteChanged -= value;
            CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// Re-query this command now. Unlike CommandManager.InvalidateRequerySuggested
    /// (which re-evaluates every command in the app on a later dispatcher pass),
    /// this is synchronous and scoped to one command. Call on the UI thread.
    /// </summary>
    public void NotifyCanExecuteChanged() => _canExecuteChanged?.Invoke(this, EventArgs.Empty);
}
