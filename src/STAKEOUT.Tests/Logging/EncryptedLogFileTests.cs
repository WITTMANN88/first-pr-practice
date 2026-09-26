using System.Text;
using Stakeout.Core;
using Stakeout.Models;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Logging;

public sealed class EncryptedLogFileTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly LogCipher _cipher = LogCipher.ForMachine("TEST-PC");

    public void Dispose() => _dir.Dispose();

    private EncryptedLogFile NewLog(string name = "test.log") => new(_dir.File(name), _cipher);

    [Fact]
    public void AppendThenReadAll_ReturnsLinesInOrder()
    {
        var log = NewLog();
        var lines = new[] { "[10:00:00] [A] [OK]", "[10:00:01] [B] [APPLIED] detail", "[10:00:02] [C] [ERROR] boom" };
        foreach (var l in lines) log.Append(l);

        Assert.Equal(lines, log.ReadAll());
    }

    [Fact]
    public void File_OnDisk_ContainsNoPlaintext()
    {
        var log = NewLog();
        log.Append("[10:00:00] [UAC (Контроль учётных записей)] [ACCESS_DENIED] HKLM\\SOFTWARE\\Secret");

        var onDisk = File.ReadAllText(log.Path, Encoding.UTF8);
        Assert.DoesNotContain("ACCESS_DENIED", onDisk);
        Assert.DoesNotContain("UAC", onDisk);
        Assert.DoesNotContain("SOFTWARE", onDisk);
    }

    [Fact]
    public void ReadAll_SkipsTamperedAndGarbageLines_KeepsTheRest()
    {
        var log = NewLog();
        log.Append("first");
        log.Append("second");
        log.Append("third");

        var raw = File.ReadAllLines(log.Path).ToList();
        var bytes = Convert.FromBase64String(raw[1]);
        bytes[^1] ^= 0xFF;                       // tamper with line 2
        raw[1] = Convert.ToBase64String(bytes);
        raw.Insert(0, "garbage, not base64");    // and add junk
        raw.Add("");                             // and a blank line
        File.WriteAllLines(log.Path, raw);

        Assert.Equal(new[] { "first", "third" }, log.ReadAll());
    }

    [Fact]
    public void ReadAll_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(NewLog("does-not-exist.log").ReadAll());
    }

    [Fact]
    public void ReadAll_WithAnotherMachinesKey_ReturnsNothing()
    {
        var log = NewLog();
        log.Append("secret");

        var foreign = new EncryptedLogFile(log.Path, LogCipher.ForMachine("OTHER-PC"));
        Assert.Empty(foreign.ReadAll());
    }

    [Fact]
    public async Task ConcurrentAppends_AreAllPersistedIntact()
    {
        var log = NewLog();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => log.Append($"line-{i:D3}"))));

        var read = log.ReadAll();
        Assert.Equal(200, read.Count);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => $"line-{i:D3}").OrderBy(x => x), read.OrderBy(x => x));
    }

    [Fact]
    public void FormatLine_ProducesSpecFormat()
    {
        var t = new DateTime(2026, 9, 26, 7, 5, 9);
        Assert.Equal("[07:05:09] [Hibernation] [APPLIED] powercfg -h off",
            EncryptedLogFile.FormatLine(t, "Hibernation", "APPLIED", "powercfg -h off"));
        Assert.Equal("[07:05:09] [RevertAll] [DONE]", EncryptedLogFile.FormatLine(t, "RevertAll", "DONE", null));
        Assert.Equal("[07:05:09] [RevertAll] [DONE]", EncryptedLogFile.FormatLine(t, "RevertAll", "DONE", "   "));
    }

    [Fact]
    public void FormatLine_FlattensLineBreaks_SoOneEntryIsOneLine()
    {
        var line = EncryptedLogFile.FormatLine(DateTime.Today, "Act\nion", "ERR\r\nOR", "multi\nline\r\ndetail");
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void FormattedLine_RoundTripsThroughLogEntryParser()
    {
        var log = NewLog();
        log.Append(EncryptedLogFile.FormatLine(new DateTime(2026, 1, 1, 23, 59, 58),
            @"Registry.SetValue SOFTWARE\Policies\X", "ACCESS_DENIED", "Requested registry access is not allowed."));

        var entry = LogEntry.Parse(log.ReadAll().Single());
        Assert.Equal("23:59:58", entry.Time);
        Assert.Equal(@"Registry.SetValue SOFTWARE\Policies\X", entry.Action);
        Assert.Equal("ACCESS_DENIED", entry.Status);
        Assert.Equal("Requested registry access is not allowed.", entry.Detail);
        Assert.Equal(LogLevel.Error, entry.Level);
    }
}
