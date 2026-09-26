using System.Windows.Threading;
using Stakeout.Core;

namespace Stakeout.Infrastructure;

/// <summary><see cref="IUiDispatcher"/> over the WPF UI thread's Dispatcher.</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Run inline when already on the UI thread, otherwise queue it.</summary>
    public void Post(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
