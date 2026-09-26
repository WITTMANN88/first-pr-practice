using Stakeout.Core;

namespace Stakeout.Tests.Common;

/// <summary>Paths are built with the host OS's rules so the same tests run on Windows and Linux.</summary>
public class ExecutableResolverTests
{
    private static readonly string Root = Path.GetFullPath(Path.GetTempPath());
    private static readonly string Trusted = Path.Combine(Root, "trusted");
    private static readonly string OnPath = Path.Combine(Root, "onpath");

    private static string JoinPath(params string[] entries) => string.Join(Path.PathSeparator, entries);

    private static Func<string, bool> Exists(params string[] files)
    {
        var set = files.ToHashSet(StringComparer.Ordinal);
        return set.Contains;
    }

    [Fact]
    public void TrustedDirectory_WinsOverPath()
    {
        var inTrusted = Path.Combine(Trusted, "tool.exe");
        var result = ExecutableResolver.Resolve("tool.exe", new[] { Trusted }, JoinPath(OnPath),
            Exists(inTrusted, Path.Combine(OnPath, "tool.exe")));
        Assert.Equal(inTrusted, result);
    }

    [Fact]
    public void FallsBackToAbsolutePathEntries_InOrder()
    {
        var second = Path.Combine(Root, "second");
        var expected = Path.Combine(second, "tool.exe");
        var result = ExecutableResolver.Resolve("tool.exe", Array.Empty<string>(),
            JoinPath(OnPath, second), Exists(expected));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("tools")]
    [InlineData("..")]
    public void RelativePathEntries_AreNeverSearched(string relative)
    {
        // Relative entries resolve against the current directory: exactly the planting risk.
        var planted = Path.Combine(relative, "tool.exe");
        var result = ExecutableResolver.Resolve("tool.exe", Array.Empty<string>(), JoinPath(relative),
            path => path == planted || path.EndsWith(planted, StringComparison.Ordinal));
        Assert.Null(result);
    }

    [Fact]
    public void RelativeTrustedDirectory_IsIgnored()
    {
        Assert.Null(ExecutableResolver.Resolve("tool.exe", new[] { "." }, null, _ => true));
    }

    [Fact]
    public void QuotedAndPaddedPathEntries_AreUnderstood()
    {
        var expected = Path.Combine(OnPath, "tool.exe");
        var result = ExecutableResolver.Resolve("tool.exe", Array.Empty<string>(),
            $" \"{OnPath}\" {Path.PathSeparator}{Path.PathSeparator}", Exists(expected));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NotInstalled_ReturnsNull()
    {
        Assert.Null(ExecutableResolver.Resolve("tool.exe", new[] { Trusted }, JoinPath(OnPath), _ => false));
        Assert.Null(ExecutableResolver.Resolve("tool.exe", Array.Empty<string>(), null, _ => true));
    }

    [Theory]
    [InlineData("sub/tool.exe")]
    [InlineData("../tool.exe")]
    [InlineData("")]
    [InlineData(" ")]
    public void OnlyBareFileNames_AreAccepted(string fileName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ExecutableResolver.Resolve(fileName, Array.Empty<string>(), null, _ => true));
    }
}
