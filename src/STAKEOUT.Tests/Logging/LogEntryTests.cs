using Stakeout.Models;

namespace Stakeout.Tests.Logging;

public class LogEntryTests
{
    [Fact]
    public void Parse_StructuredLine_SplitsFields()
    {
        var e = LogEntry.Parse("[12:00:01] [UAC (Контроль учётных записей)] [APPLIED] needs reboot | really");
        Assert.Equal("12:00:01", e.Time);
        Assert.Equal("UAC (Контроль учётных записей)", e.Action);
        Assert.Equal("APPLIED", e.Status);
        Assert.Equal("needs reboot | really", e.Detail);
    }

    [Fact]
    public void Parse_NoDetail_GivesEmptyDetail()
    {
        var e = LogEntry.Parse("[12:00:01] [RevertAll] [DONE]");
        Assert.Equal("DONE", e.Status);
        Assert.Equal("", e.Detail);
    }

    [Fact]
    public void Parse_ActionContainingBracket_StillFindsStatus()
    {
        var e = LogEntry.Parse("[12:00:01] [Path [x86] tweak] [OK] fine");
        Assert.Equal("Path [x86] tweak", e.Action);
        Assert.Equal("OK", e.Status);
    }

    [Theory]
    [InlineData("ERROR", LogLevel.Error)]
    [InlineData("ACCESS_DENIED", LogLevel.Error)]
    [InlineData("TIMEOUT", LogLevel.Error)]
    [InlineData("FAILED", LogLevel.Error)]
    [InlineData("REVERT-PARTIAL", LogLevel.Error)]
    [InlineData("access_denied", LogLevel.Error)]
    [InlineData("OK", LogLevel.Success)]
    [InlineData("APPLIED", LogLevel.Success)]
    [InlineData("REVERTED", LogLevel.Success)]
    [InlineData("DONE", LogLevel.Success)]
    [InlineData("RUN", LogLevel.Info)]
    [InlineData("START", LogLevel.Info)]
    public void Parse_ClassifiesStatus(string status, LogLevel expected)
    {
        Assert.Equal(expected, LogEntry.Parse($"[00:00:00] [X] [{status}]").Level);
    }

    [Theory]
    [InlineData("free text without structure")]
    [InlineData("[12:00] missing brackets")]
    [InlineData("")]
    public void Parse_UnstructuredLine_KeepsTextAsDetail(string line)
    {
        var e = LogEntry.Parse(line);
        Assert.Equal(line, e.Detail);
        Assert.Equal(LogLevel.Info, e.Level);
        Assert.Equal("", e.Status);
    }
}
