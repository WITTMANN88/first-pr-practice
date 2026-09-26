using System.Collections.ObjectModel;

namespace Stakeout.Core;

public enum NotificationKind { Info, Success, Warning, Error }

/// <summary>One toast. Immutable.</summary>
public sealed class Notification
{
    public Notification(string message, NotificationKind kind)
    {
        Message = message ?? string.Empty;
        Kind = kind;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Message { get; }
    public NotificationKind Kind { get; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
}

/// <summary>
/// Sending side: what view models and services depend on (constructor-injected).
/// One method keeps fakes trivial; typed helpers are extension methods below.
/// </summary>
public interface INotificationService
{
    void Show(string message, NotificationKind kind);
}

public static class NotificationServiceExtensions
{
    public static void Success(this INotificationService n, string message) => n.Show(message, NotificationKind.Success);
    public static void Error(this INotificationService n, string message) => n.Show(message, NotificationKind.Error);
    public static void Warning(this INotificationService n, string message) => n.Show(message, NotificationKind.Warning);
    public static void Info(this INotificationService n, string message) => n.Show(message, NotificationKind.Info);
}

/// <summary>Presenting side: the shell binds its toast stack to <see cref="Active"/>.</summary>
public interface INotificationFeed
{
    ReadOnlyObservableCollection<Notification> Active { get; }
    void Dismiss(Guid id);
}

/// <summary>
/// Marshals work onto the UI thread. The app implements it over the WPF
/// Dispatcher; tests run actions inline. Keeps the core free of WPF.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
