namespace Stakeout.Core;

/// <summary>
/// Event handlers that reference their subscriber weakly.
///
/// A normal <c>publisher.Event += subscriber.Method</c> stores a strong reference
/// to the subscriber inside the publisher, so a long-lived publisher keeps a
/// short-lived subscriber alive. The worst case in WPF is a running
/// <c>DispatcherTimer</c>: the Dispatcher roots every running timer, so its Tick
/// handler roots the subscriber for the life of the process.
///
/// The handler returned here holds the subscriber through a
/// <see cref="WeakReference{T}"/>. Once the subscriber has been collected, the
/// next event calls <c>detach</c> so the publisher can drop the dead handler
/// (for a timer: stop it and unsubscribe).
///
/// The callback must not capture the subscriber, or the weak reference is
/// pointless: pass a <c>static</c> lambda that receives it as its first argument,
/// e.g. <c>static (vm, sender, e) =&gt; vm.OnTick()</c>. The compiler rejects
/// captures in a static lambda.
/// </summary>
public static class WeakHandler
{
    public static EventHandler Create<TTarget>(
        TTarget target,
        Action<TTarget, object?, EventArgs> onEvent,
        Action<object?, EventHandler>? detach = null)
        where TTarget : class
    {
        var relay = new Relay<TTarget, EventArgs>(target, onEvent);
        EventHandler handler = null!;
        handler = (sender, e) =>
        {
            if (!relay.TryInvoke(sender, e)) detach?.Invoke(sender, handler);
        };
        return handler;
    }

    public static EventHandler<TArgs> Create<TTarget, TArgs>(
        TTarget target,
        Action<TTarget, object?, TArgs> onEvent,
        Action<object?, EventHandler<TArgs>>? detach = null)
        where TTarget : class
    {
        var relay = new Relay<TTarget, TArgs>(target, onEvent);
        EventHandler<TArgs> handler = null!;
        handler = (sender, e) =>
        {
            if (!relay.TryInvoke(sender, e)) detach?.Invoke(sender, handler);
        };
        return handler;
    }

    private sealed class Relay<TTarget, TArgs> where TTarget : class
    {
        private readonly WeakReference<TTarget> _target;
        private readonly Action<TTarget, object?, TArgs> _onEvent;

        public Relay(TTarget target, Action<TTarget, object?, TArgs> onEvent)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(onEvent);
            // An instance method of the target would hold it strongly via Delegate.Target.
            if (ReferenceEquals(onEvent.Target, target))
                throw new ArgumentException("The callback must not be bound to the target; use a static lambda.", nameof(onEvent));
            _target = new WeakReference<TTarget>(target);
            _onEvent = onEvent;
        }

        /// <summary>Invoke on the live target; false once it has been collected.</summary>
        public bool TryInvoke(object? sender, TArgs e)
        {
            if (!_target.TryGetTarget(out var target)) return false;
            _onEvent(target, sender, e);
            return true;
        }
    }
}
