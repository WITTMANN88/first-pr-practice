using System.Collections.ObjectModel;

namespace Stakeout.Core;

/// <summary>
/// Central notification (toast) service.
///   * Callable from any thread: collection changes are posted to the UI thread.
///   * Each toast auto-expires after <see cref="Lifetime"/> (5 s by default).
///   * At most <see cref="MaxVisible"/> toasts are stacked; the oldest is dropped
///     first, so a burst (e.g. removing 30 apps) cannot flood the screen.
///   * Every notification is also written to the encrypted log.
/// The delay function is injectable so expiry can be tested without real waits.
/// </summary>
public sealed class NotificationService : INotificationService, INotificationFeed, IDisposable
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(5);
    public const int DefaultMaxVisible = 4;

    private readonly IUiDispatcher _dispatcher;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ObservableCollection<Notification> _items = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    public NotificationService(IUiDispatcher dispatcher)
        : this(dispatcher, DefaultLifetime, DefaultMaxVisible, Task.Delay) { }

    public NotificationService(IUiDispatcher dispatcher, TimeSpan lifetime, int maxVisible,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVisible, 1);
        Lifetime = lifetime;
        MaxVisible = maxVisible;
        Active = new ReadOnlyObservableCollection<Notification>(_items);
    }

    public TimeSpan Lifetime { get; }
    public int MaxVisible { get; }
    public ReadOnlyObservableCollection<Notification> Active { get; }

    public void Show(string message, NotificationKind kind)
    {
        var n = new Notification(message, kind);
        Logger.Log("Notify", kind.ToString().ToUpperInvariant(), message);

        _dispatcher.Post(() =>
        {
            _items.Add(n);
            while (_items.Count > MaxVisible) _items.RemoveAt(0);
        });

        _ = ExpireAsync(n);
    }

    public void Dismiss(Guid id) => _dispatcher.Post(() =>
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id != id) continue;
            _items.RemoveAt(i);
            return;
        }
    });

    private async Task ExpireAsync(Notification n)
    {
        CancellationToken token;
        try
        {
            token = _shutdown.Token;
        }
        catch (ObjectDisposedException)
        {
            return; // shown after shutdown: nothing left to expire
        }

        try
        {
            await _delay(Lifetime, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // shutting down
        }
        Dismiss(n.Id);
    }

    /// <summary>
    /// Cancel pending expirations on shutdown. Idempotent; a late Show() still
    /// displays and logs its toast but schedules no expiry.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
