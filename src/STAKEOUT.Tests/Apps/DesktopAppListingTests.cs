using Stakeout.Models;
using Stakeout.Services;

namespace Stakeout.Tests.Apps;

public class DesktopAppListingTests
{
    private static UninstallEntry Entry(string key, UninstallSource source = UninstallSource.LocalMachine, params (string Name, object Value)[] values)
        => new(source, key, values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase));

    private static UninstallEntry Program(string key, string name, UninstallSource source = UninstallSource.LocalMachine, params (string, object)[] extra)
        => Entry(key, source, Base(name).Concat(extra).ToArray());

    private static (string, object)[] Base(string name)
        => new (string, object)[] { ("DisplayName", name), ("UninstallString", $@"""C:\Program Files\{name}\uninstall.exe""") };

    [Fact]
    public void Lists_ProgramsWithNameAndUninstallCommand()
    {
        var apps = DesktopAppListing.Parse(new[]
        {
            Program("7-Zip", "7-Zip 24.08 (x64)", UninstallSource.LocalMachine, ("Publisher", "Igor Pavlov"), ("DisplayVersion", "24.08"), ("EstimatedSize", 5947)),
            Entry("NoName", values: ("UninstallString", @"C:\x\u.exe")),
            Entry("NoUninstaller", values: ("DisplayName", "Orphan")),
        });

        var app = Assert.Single(apps);
        Assert.Equal(AppKind.Desktop, app.Kind);
        Assert.Equal(AppCategory.Desktop, app.Category);
        Assert.Equal("7-Zip 24.08 (x64)", app.DisplayName);
        Assert.Equal("Igor Pavlov", app.Publisher);
        Assert.Equal("24.08", app.Version);
        Assert.Equal(5947L * 1024, app.SizeBytes);
        Assert.False(app.IsCritical);
    }

    [Fact]
    public void QuietUninstallString_UsedOnlyWhenThereIsNoOther()
    {
        var both = Entry("A", UninstallSource.LocalMachine, ("DisplayName", "A"), ("UninstallString", "a.exe"), ("QuietUninstallString", "a.exe /S"));
        var quietOnly = Entry("B", UninstallSource.LocalMachine, ("DisplayName", "B"), ("QuietUninstallString", "b.exe /S"));

        var apps = DesktopAppListing.Parse(new[] { both, quietOnly });

        Assert.Equal("a.exe", apps.Single(a => a.Name == "A").UninstallCommand);
        Assert.Equal("b.exe /S", apps.Single(a => a.Name == "B").UninstallCommand);
    }

    [Fact]
    public void Hides_SystemComponents()
    {
        Assert.Empty(DesktopAppListing.Parse(new[] { Program("{GUID}", "Microsoft Edge Update", extra: ("SystemComponent", 1)) }));
        Assert.Single(DesktopAppListing.Parse(new[] { Program("{GUID}", "Visible", extra: ("SystemComponent", 0)) }));
    }

    [Theory]
    [InlineData("KB5005565")]
    [InlineData("kb2468871")]
    public void Hides_UpdatesByKeyName(string key)
    {
        Assert.Empty(DesktopAppListing.Parse(new[] { Program(key, "Security Update for Windows") }));
    }

    [Fact]
    public void KeyStartingWithKbWithoutNumber_IsNotAnUpdate()
    {
        Assert.Single(DesktopAppListing.Parse(new[] { Program("KBackup", "KBackup") }));
    }

    [Fact]
    public void Hides_UpdatesByParentOrReleaseType()
    {
        Assert.Empty(DesktopAppListing.Parse(new[]
        {
            Program("{P1}", "Office patch", extra: ("ParentKeyName", "Office16.PROPLUS")),
            Program("{P2}", "Hotfix", extra: ("ReleaseType", "Security Update")),
        }));
    }

    [Fact]
    public void SameProgramInSeveralBranches_ListedOnce_MachineWideFirst()
    {
        var apps = DesktopAppListing.Parse(new[]
        {
            Program("Steam", "Steam", UninstallSource.CurrentUser),
            Program("Steam", "Steam", UninstallSource.LocalMachine32),
        });

        Assert.Equal(UninstallSource.LocalMachine32, Assert.Single(apps).Source);
    }

    [Fact]
    public void SameNameDifferentVersion_BothListed_SortedByName()
    {
        var apps = DesktopAppListing.Parse(new[]
        {
            Program("vc2", "Microsoft Visual C++ 2015-2022 (x86)", extra: ("DisplayVersion", "14.40")),
            Program("chrome", "Google Chrome"),
            Program("vc1", "Microsoft Visual C++ 2015-2022 (x86)", extra: ("DisplayVersion", "14.38")),
        });

        Assert.Equal(3, apps.Count);
        Assert.Equal("Google Chrome", apps[0].DisplayName);
    }

    [Fact]
    public void PerUserEntries_AreFlagged()
    {
        var app = Assert.Single(DesktopAppListing.Parse(new[] { Program("Discord", "Discord", UninstallSource.CurrentUser) }));
        Assert.True(app.IsPerUser);
    }

    [Fact]
    public void DisplayIcon_IsSplitIntoPathAndIndex()
    {
        var app = Assert.Single(DesktopAppListing.Parse(new[] { Program("chrome", "Google Chrome", extra: ("DisplayIcon", @"C:\Program Files\Google\Chrome\Application\chrome.exe,0")) }));
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", app.IconPath);
        Assert.Equal(0, app.IconIndex);
    }

    [Fact]
    public void EstimatedSize_AsString_IsAccepted_NegativeIgnored()
    {
        Assert.Equal(2048, DesktopAppListing.Parse(new[] { Program("a", "A", extra: ("EstimatedSize", "2")) })[0].SizeBytes);
        Assert.Equal(0, DesktopAppListing.Parse(new[] { Program("b", "B", extra: ("EstimatedSize", "junk")) })[0].SizeBytes);
    }
}

public class DisplayIconParserTests
{
    [Theory]
    [InlineData(@"C:\App\app.exe,0", @"C:\App\app.exe", 0)]
    [InlineData(@"C:\App\app.exe, 2", @"C:\App\app.exe", 2)]
    [InlineData(@"""C:\Program Files\App\app.exe"",-101", @"C:\Program Files\App\app.exe", -101)]
    [InlineData(@"""C:\Program Files\App\app.ico""", @"C:\Program Files\App\app.ico", 0)]
    [InlineData(@"C:\Program Files\App\app.ico", @"C:\Program Files\App\app.ico", 0)]
    [InlineData(@"%SystemRoot%\system32\shell32.dll,-16770", @"%SystemRoot%\system32\shell32.dll", -16770)]
    [InlineData(@"C:\Odd,Folder\app.exe", @"C:\Odd,Folder\app.exe", 0)] // a comma that is not an index
    public void Parses(string text, string path, int index)
    {
        Assert.Equal((path, index), DisplayIconParser.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    public void NoPath_GivesNull(string? text)
    {
        Assert.Null(DisplayIconParser.Parse(text));
    }
}

public class UninstallCommandTests
{
    private static readonly HashSet<string> Existing = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files\Steam\uninstall.exe",
        @"C:\Program Files\My App\Uninstall App.exe",
        @"C:\Program Files (x86)\Tool\unins000.exe",
    };

    private static UninstallCommand? Parse(string text) => UninstallCommand.Parse(text, Existing.Contains);

    [Fact]
    public void QuotedPath_WithArguments()
    {
        Assert.Equal(new UninstallCommand(@"C:\Program Files (x86)\Tool\unins000.exe", "/SILENT"),
            Parse(@"""C:\Program Files (x86)\Tool\unins000.exe"" /SILENT"));
    }

    [Fact]
    public void QuotedPath_ThatDoesNotExist_IsRejected()
    {
        Assert.Null(Parse(@"""C:\Gone\uninstall.exe"""));
    }

    [Fact]
    public void UnquotedPathWithSpaces_FindsTheExistingExe()
    {
        Assert.Equal(new UninstallCommand(@"C:\Program Files\My App\Uninstall App.exe", "/x"),
            Parse(@"C:\Program Files\My App\Uninstall App.exe /x"));
        Assert.Equal(new UninstallCommand(@"C:\Program Files\Steam\uninstall.exe", ""),
            Parse(@"C:\Program Files\Steam\uninstall.exe"));
    }

    [Fact]
    public void UnquotedPath_NothingExists_IsRejected()
    {
        Assert.Null(Parse(@"C:\Nope\a.exe /x"));
    }

    [Theory]
    [InlineData("MsiExec.exe /I{23170F69-40C1-2702-2408-000001000000}", "/X{23170F69-40C1-2702-2408-000001000000}")]
    [InlineData("MsiExec.exe /X{23170F69-40C1-2702-2408-000001000000}", "/X{23170F69-40C1-2702-2408-000001000000}")]
    [InlineData("msiexec /i {ABC}", "/X {ABC}")]
    [InlineData("MsiExec.exe /I{ABC} /qb", "/X{ABC} /qb")]
    public void MsiExec_AlwaysUninstalls(string text, string args)
    {
        var cmd = Parse(text);
        Assert.Equal(new UninstallCommand("msiexec.exe", args), cmd);
        Assert.True(cmd!.IsSystemTool);
    }

    [Fact]
    public void BareSystemTool()
    {
        var cmd = Parse(@"RunDll32 C:\PROGRA~1\COMMON~1\INSTAL~1\PROFES~1\RunTime\11\50\Intel32\Ctor.dll,LaunchSetup ""C:\Program Files\x\setup.exe""");
        Assert.Equal("RunDll32.exe", cmd!.FileName);
        Assert.StartsWith(@"C:\PROGRA~1", cmd.Arguments, StringComparison.Ordinal);
        Assert.True(cmd.IsSystemTool);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData(@"..\evil\uninstall.exe")]
    [InlineData(@"""..\evil\uninstall.exe""")]
    public void Garbage_IsRejected(string? text)
    {
        Assert.Null(Parse(text!));
    }
}
