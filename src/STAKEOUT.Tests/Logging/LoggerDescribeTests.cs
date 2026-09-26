using Stakeout.Core;

namespace Stakeout.Tests.Logging;

public class LoggerDescribeTests
{
    [Fact]
    public void Describe_NamesTypeAndMessage()
    {
        Assert.Equal("System.InvalidOperationException: broken",
            Logger.Describe(new InvalidOperationException("broken")));
    }

    [Fact]
    public void Describe_EmptyMessage_StillNamesType()
    {
        // The trimmed build logged "[Startup] [ERROR]" with nothing after it:
        // an exception with a blank message must never produce a blank line.
        Assert.Equal("System.NotSupportedException", Logger.Describe(new NotSupportedException("")));
        Assert.Equal("System.NotSupportedException", Logger.Describe(new NotSupportedException(" \r\n ")));
    }

    [Fact]
    public void Describe_WalksInnerChain_OutermostFirst()
    {
        var ex = new InvalidOperationException("outer",
            new System.Reflection.TargetInvocationException(new NotSupportedException("root cause")));

        var text = Logger.Describe(ex);

        Assert.StartsWith("System.InvalidOperationException: outer → System.Reflection.TargetInvocationException", text, StringComparison.Ordinal);
        Assert.EndsWith(" → System.NotSupportedException: root cause", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_StopsAfterBoundedDepth()
    {
        Exception ex = new InvalidOperationException("0");
        for (var i = 1; i < 50; i++) ex = new InvalidOperationException(i.ToString(System.Globalization.CultureInfo.InvariantCulture), ex);

        var levels = Logger.Describe(ex).Split(" → ");

        Assert.Equal(8, levels.Length);
        Assert.Equal("System.InvalidOperationException: 49", levels[0]);
    }

    [Fact]
    public void Describe_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Logger.Describe(null!));
    }
}
