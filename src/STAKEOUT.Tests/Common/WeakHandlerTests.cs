using System.Runtime.CompilerServices;
using Stakeout.Core;

namespace Stakeout.Tests.Common;

public class WeakHandlerTests
{
    /// <summary>Stands in for a long-lived publisher (a running DispatcherTimer, a singleton service).</summary>
    private sealed class Publisher
    {
        public event EventHandler? Tick;
        public event EventHandler<int>? Value;
        public int TickSubscribers => Tick?.GetInvocationList().Length ?? 0;
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
        public void RaiseValue(int v) => Value?.Invoke(this, v);
        public void Unsubscribe(EventHandler h) => Tick -= h;
    }

    private sealed class Subscriber
    {
        public int Ticks;
        public int LastValue;
    }

    private static void ForceGc()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // NoInlining: the subscriber must not survive in a JIT-extended local of the test method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeStrongly(Publisher p)
    {
        var s = new Subscriber();
        p.Tick += (_, _) => s.Ticks++;
        return new WeakReference(s);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeWeakly(Publisher p)
    {
        var s = new Subscriber();
        p.Tick += WeakHandler.Create(s, static (sub, _, _) => sub.Ticks++,
            static (sender, h) => ((Publisher)sender!).Unsubscribe(h));
        return new WeakReference(s);
    }

    [Fact]
    public void Baseline_StrongSubscription_KeepsTheSubscriberAlive()
    {
        var publisher = new Publisher();
        var subscriber = SubscribeStrongly(publisher);
        ForceGc();
        Assert.True(subscriber.IsAlive);      // the leak this helper exists to prevent
        GC.KeepAlive(publisher);
    }

    [Fact]
    public void WeakSubscription_DoesNotKeepTheSubscriberAlive()
    {
        var publisher = new Publisher();
        var subscriber = SubscribeWeakly(publisher);
        ForceGc();
        Assert.False(subscriber.IsAlive);
        GC.KeepAlive(publisher);
    }

    [Fact]
    public void WhileAlive_EventsReachTheSubscriber()
    {
        var publisher = new Publisher();
        var s = new Subscriber();
        publisher.Tick += WeakHandler.Create(s, static (sub, _, _) => sub.Ticks++);

        publisher.RaiseTick();
        publisher.RaiseTick();

        Assert.Equal(2, s.Ticks);
    }

    [Fact]
    public void AfterCollection_NextEventDetachesTheDeadHandler_Once()
    {
        var publisher = new Publisher();
        _ = SubscribeWeakly(publisher);
        Assert.Equal(1, publisher.TickSubscribers);
        ForceGc();

        publisher.RaiseTick();       // finds the target gone → detach
        publisher.RaiseTick();       // nothing left to call

        Assert.Equal(0, publisher.TickSubscribers);
    }

    [Fact]
    public void Detach_ReceivesTheSenderAndTheSubscribedHandler()
    {
        var publisher = new Publisher();
        object? seenSender = null;
        EventHandler? seenHandler = null;
        var handler = CreateWithDeadTarget((sender, h) => { seenSender = sender; seenHandler = h; });
        publisher.Tick += handler;
        ForceGc();

        publisher.RaiseTick();

        Assert.Same(publisher, seenSender);
        Assert.Same(handler, seenHandler);   // the exact delegate, so "-=" removes it
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EventHandler CreateWithDeadTarget(Action<object?, EventHandler> detach)
        => WeakHandler.Create(new Subscriber(), static (sub, _, _) => sub.Ticks++, detach);

    [Fact]
    public void GenericVariant_PassesTheEventArgs()
    {
        var publisher = new Publisher();
        var s = new Subscriber();
        publisher.Value += WeakHandler.Create<Subscriber, int>(s, static (sub, _, v) => sub.LastValue = v);

        publisher.RaiseValue(42);

        Assert.Equal(42, s.LastValue);
    }

    [Fact]
    public void CallbackBoundToTheTarget_IsRejected()
    {
        var s = new BoundSubscriber();
        Assert.Throws<ArgumentException>(() => WeakHandler.Create<BoundSubscriber>(s, s.Handle));
    }

    private sealed class BoundSubscriber
    {
        public (BoundSubscriber Self, object? Sender, EventArgs Args)? Last { get; private set; }

        // An instance method: the delegate's Target is this object, which is what must be rejected.
        public void Handle(BoundSubscriber self, object? sender, EventArgs e) => Last = (self, sender, e);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => WeakHandler.Create<Subscriber>(null!, static (_, _, _) => { }));
        Assert.Throws<ArgumentNullException>(() => WeakHandler.Create(new Subscriber(), null!));
    }
}
