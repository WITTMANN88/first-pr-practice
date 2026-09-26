using System.Text.RegularExpressions;

namespace Stakeout.Tests.Xaml;

/// <summary>
/// Source-level checks on the WPF project's XAML. The test project cannot load
/// WPF (it runs on any OS), and the XAML compiler ignores both d: attributes and
/// binding paths, so without these a typo only shows up in the designer or as a
/// silently empty control at runtime (this is how a toast bound to a
/// non-existent "Text" property shipped with no message).
/// </summary>
public class XamlLintTests
{
    private static readonly string AppDir = FindAppDir();

    private static string FindAppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "STAKEOUT");
            if (File.Exists(Path.Combine(candidate, "STAKEOUT.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("src/STAKEOUT not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The shell and every page/view file; resource dictionaries and controls are excluded.</summary>
    private static IEnumerable<string> ScreenFiles() =>
        new[] { "MainWindow.xaml" }.Concat(
            Directory.GetFiles(Path.Combine(AppDir, "Views"), "*.xaml").OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => Path.Combine("Views", Path.GetFileName(f))));

    public static TheoryData<string> Screens() => new(ScreenFiles());

    private static string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static IEnumerable<string> SourceFiles() =>
        new[] { AppDir, Path.Combine(AppDir, "..", "STAKEOUT.Core") }
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f));

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryScreen_HasDesignTimeData(string file)
    {
        var xaml = Read(file);
        Assert.Contains("mc:Ignorable=\"d\"", xaml, StringComparison.Ordinal);
        Assert.Matches(@"d:DataContext=""\{x:Static design:DesignData\.\w+\}""", xaml);
    }

    [Fact]
    public void DesignDataReferences_NameExistingProperties()
    {
        var declared = Regex.Matches(Read(Path.Combine("Design", "DesignData.cs")),
                @"public static \w+ (\w+) =>")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.NotEmpty(declared);

        foreach (var file in ScreenFiles())
        {
            foreach (Match m in Regex.Matches(Read(file), @"DesignData\.(\w+)"))
                Assert.True(declared.Contains(m.Groups[1].Value), $"{file}: DesignData.{m.Groups[1].Value} does not exist");
        }
    }

    /// <summary>
    /// Coarse but effective: every segment of every binding path must be the name
    /// of a public property declared somewhere in the app or core sources. It does
    /// not know which type a binding targets, so it catches names that exist
    /// nowhere (typos, renamed properties), not properties on the wrong type.
    /// </summary>
    [Fact]
    public void BindingPaths_NamePropertiesThatExist()
    {
        var properties = SourceFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f),
                @"public\s+(?:static\s+|override\s+|virtual\s+)*[\w<>\[\]?,. ]+?\s+(\w+)\s*(?:\{|=>)").Select(m => m.Groups[1].Value))
            .ToHashSet();

        var problems = new List<string>();
        var xamlFiles = Directory.GetFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f));
        foreach (var file in xamlFiles)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{Binding\b([^{}]*)\}"))
            {
                var parts = m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                // Paths relative to an element or another source name framework properties: out of scope.
                if (parts.Any(p => p.StartsWith("ElementName=", StringComparison.Ordinal) ||
                                   p.StartsWith("RelativeSource=", StringComparison.Ordinal) ||
                                   p.StartsWith("Source=", StringComparison.Ordinal)))
                {
                    continue;
                }
                var path = parts.FirstOrDefault(p => !p.Contains('=', StringComparison.Ordinal)) ??
                           parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.Ordinal))?["Path=".Length..];
                if (string.IsNullOrEmpty(path) || path == ".") continue;

                foreach (var segment in path.Split('.'))
                {
                    if (!properties.Contains(Regex.Replace(segment, @"\[.*\]$", "")))
                        problems.Add($"{Path.GetFileName(file)}: {{Binding {path}}} — no property named '{segment}'");
                }
            }
        }
        Assert.Empty(problems);
    }
}
