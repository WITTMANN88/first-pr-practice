using System.Diagnostics;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Processes;

/// <summary>
/// Real child processes (sh on Linux/macOS, cmd on Windows), so the kill and
/// detach behaviour is exercised end to end, not mocked.
/// </summary>
public sealed class ProcessRunnerTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Shell => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
        : "/bin/sh";

    /// <summary>Arguments for the platform shell: sleep ~<paramref name="delaySeconds"/> s, then run the given command.</summary>
    private static string Script(int delaySeconds, string unixThen, string windowsThen) => OperatingSystem.IsWindows()
        ? $"/d /c \"ping -n {delaySeconds + 1} 127.0.0.1 >nul & {windowsThen}\""
        : $"-c \"sleep {delaySeconds}; {unixThen}\"";

    private string Marker => _dir.File("done.marker");

    private string CreateMarker => OperatingSystem.IsWindows() ? $"echo x> {Marker}" : $"touch '{Marker}'";

    private static async Task<bool> EventuallyExists(string path, TimeSpan within)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < within)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(100);
        }
        return File.Exists(path);
    }

    [Fact]
    public async Task Completed_ReturnsExitCodeAndBothStreams()
    {
        var args = OperatingSystem.IsWindows()
            ? "/d /c \"echo hello& echo oops 1>&2& exit /b 3\""
            : "-c \"echo hello; echo oops 1>&2; exit 3\"";

        var result = await ProcessRunner.RunAsync(Shell, args);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("hello", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("oops", result.StdErr, StringComparison.Ordinal);
        Assert.False(result.Detached);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Timeout_WithKill_KillsTheProcessTree()
    {
        var result = await ProcessRunner.RunAsync(Shell, Script(2, CreateMarker, CreateMarker), timeoutMs: 300);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timeout", result.StdErr, StringComparison.Ordinal);
        Assert.False(result.Detached);
        Assert.False(await EventuallyExists(Marker, TimeSpan.FromSeconds(3.5)));   // never got to finish
    }

    [Fact]
    public async Task Cancel_WithKill_ThrowsAndKills()
    {
        using var cts = new CancellationTokenSource(300);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessRunner.RunAsync(Shell, Script(2, CreateMarker, CreateMarker), ct: cts.Token));

        Assert.False(await EventuallyExists(Marker, TimeSpan.FromSeconds(3.5)));
    }

    [Fact]
    public async Task Cancel_WithDetach_ReturnsAtOnce_AndTheProcessFinishesOnItsOwn()
    {
        // After the detach the child writes 2000 lines. All of them must still be
        // captured: that proves the runner kept draining the pipe instead of
        // closing it (a closed pipe fails the child's writes; winget writes
        // progress to stdout during the whole installation).
        var flood = OperatingSystem.IsWindows()
            ? $"for /l %i in (1,1,2000) do @echo line %i& {CreateMarker}"
            : $"i=0; while [ $i -lt 2000 ]; do echo line $i; i=$((i+1)); done; {CreateMarker}";
        using var cts = new CancellationTokenSource(300);
        var sw = Stopwatch.StartNew();

        var result = await ProcessRunner.RunAsync(Shell, Script(1, flood, flood),
            onAbandon: AbandonPolicy.Detach, ct: cts.Token);

        Assert.True(result.Detached);
        Assert.False(result.Success);
        Assert.Contains("cancelled", result.StdErr, StringComparison.Ordinal);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"returned after {sw.Elapsed}");
        Assert.NotNull(result.Completion);
        var final = await result.Completion!.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, final.ExitCode);
        Assert.Equal(2000, final.StdOut.Split('\n').Count(l => l.StartsWith("line ", StringComparison.Ordinal)));
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public async Task Timeout_WithDetach_LeavesTheProcessRunning()
    {
        var result = await ProcessRunner.RunAsync(Shell, Script(1, CreateMarker, CreateMarker),
            timeoutMs: 300, onAbandon: AbandonPolicy.Detach);

        Assert.True(result.Detached);
        Assert.Contains("timeout", result.StdErr, StringComparison.Ordinal);
        var final = await result.Completion!.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, final.ExitCode);
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public async Task RelativePath_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ProcessRunner.RunAsync("powercfg.exe", "/list"));
    }

    [Fact]
    public async Task MissingExecutable_IsAFailedResult_NotAnException()
    {
        var result = await ProcessRunner.RunAsync(_dir.File("no-such-tool.exe"), "");
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.Success);
    }
}
