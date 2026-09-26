using Stakeout.Core;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Notifications;

public class NotificationServiceTests
{
    private readonly ManualDelay _delay = new();

    private NotificationService Create(IUiDispatcher? dispatcher = null, int maxVisible = 4)
        => new(dispatcher ?? new InlineDispatcher(), TimeSpan.FromSeconds(5), maxVisible, _delay.Delay);

    [Fact]
    public void TypedHelpers_ProduceMatchingKinds()
    {
        using var svc = Create();
        INotificationService n = svc;

        n.Success("ok");
        n.Error("bad");
        n.Warning("hmm");
        n.Info("fyi");

        Assert.Equal(
            new[] { ("ok", NotificationKind.Success), ("bad", NotificationKind.Error),
                    ("hmm", NotificationKind.Warning), ("fyi", NotificationKind.Info) },
            svc.Active.Select(x => (x.Message, x.Kind)));
    }

    [Fact]
    public async Task Notification_ExpiresAfterLifetime()
    {
        using var svc = Create();
        svc.Success("done");
        Assert.Single(svc.Active);

        _delay.ElapseAll();
        await Wait.UntilAsync(() => svc.Active.Count == 0);
    }

    [Fact]
    public void Stack_IsCapped_OldestDroppedFirst()
    {
        using var svc = Create(maxVisible: 3);
        for (var i = 1; i <= 5; i++) svc.Info($"n{i}");

        Assert.Equal(new[] { "n3", "n4", "n5" }, svc.Active.Select(x => x.Message));
    }

    [Fact]
    public void Dismiss_RemovesOnlyThatNotification()
    {
        using var svc = Create();
        svc.Info("a");
        svc.Info("b");
        var a = svc.Active[0];

        svc.Dismiss(a.Id);

        Assert.Equal("b", Assert.Single(svc.Active).Message);
    }

    [Fact]
    public void Dismiss_UnknownId_IsNoOp()
    {
        using var svc = Create();
        svc.Info("a");
        svc.Dismiss(Guid.NewGuid());
        Assert.Single(svc.Active);
    }

    [Fact]
    public async Task Show_FromBackgroundThreads_OnlyMutatesOnTheDispatcher()
    {
        var ui = new QueueDispatcher();
        using var svc = Create(ui, maxVisible: 100);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => svc.Info($"bg{i}"))));

        Assert.Empty(svc.Active);      // nothing touched the collection off the "UI thread"
        Assert.Equal(20, ui.Pending);
        ui.RunAll();
        Assert.Equal(20, svc.Active.Count);
    }

    [Fact]
    public async Task Dispose_CancelsPendingExpirations_AndLateShowDoesNotThrow()
    {
        var svc = Create();
        svc.Info("stays");
        svc.Dispose();

        _delay.ElapseAll();             // too late: expirations were cancelled
        await Task.Delay(20);
        Assert.Single(svc.Active);

        var ex = Record.Exception(() => svc.Info("after dispose"));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new NotificationService(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NotificationService(new InlineDispatcher(), TimeSpan.FromSeconds(1), 0, _delay.Delay));
    }

    [Fact]
    public void DefaultLifetime_IsFiveSeconds()
    {
        using var svc = new NotificationService(new InlineDispatcher());
        Assert.Equal(TimeSpan.FromSeconds(5), svc.Lifetime);
    }
}
