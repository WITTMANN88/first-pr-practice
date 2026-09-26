using System.Diagnostics;
using Stakeout.Core;

namespace Stakeout.Tests.Common;

public class TimeoutGuardTests
{
    [Fact]
    public async Task CompletedInTime_ReturnsTheResult()
    {
        Assert.Equal(42, await TimeoutGuard.Await(Task.FromResult(42), TimeSpan.FromSeconds(1), -1, "t"));
    }

    [Fact]
    public async Task TooSlow_ReturnsFallbackPromptly()
    {
        var never = new TaskCompletionSource<int>().Task;
        var sw = Stopwatch.StartNew();

        var result = await TimeoutGuard.Await(never, TimeSpan.FromMilliseconds(50), -1, "t");

        Assert.Equal(-1, result);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Faulted_ReturnsFallbackInsteadOfThrowing()
    {
        var faulted = Task.FromException<int>(new InvalidOperationException("boom"));
        Assert.Equal(-1, await TimeoutGuard.Await(faulted, TimeSpan.FromSeconds(1), -1, "t"));
    }
}
