using Stakeout.Models;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Uwp;

public class UwpCatalogTests
{
    [Theory]
    // Preinstalled / sponsored junk
    [InlineData("Microsoft.BingNews", AppCategory.Bloatware)]
    [InlineData("Microsoft.BingWeather", AppCategory.Bloatware)]
    [InlineData("Microsoft.GetHelp", AppCategory.Bloatware)]
    [InlineData("Microsoft.Getstarted", AppCategory.Bloatware)]
    [InlineData("Microsoft.MicrosoftOfficeHub", AppCategory.Bloatware)]
    [InlineData("Microsoft.WindowsFeedbackHub", AppCategory.Bloatware)]
    [InlineData("Microsoft.MSPaint", AppCategory.Bloatware)]           // Paint 3D
    [InlineData("Microsoft.Windows.DevHome", AppCategory.Bloatware)]   // beats the generic Microsoft.Windows.* rule
    [InlineData("MicrosoftWindows.Client.WebExperience", AppCategory.Bloatware)]
    [InlineData("Clipchamp.Clipchamp", AppCategory.Bloatware)]
    [InlineData("king.com.CandyCrushSaga", AppCategory.Bloatware)]
    [InlineData("SpotifyAB.SpotifyMusic", AppCategory.Bloatware)]
    [InlineData("4DF9E0F8.Netflix", AppCategory.Bloatware)]
    [InlineData("Disney.37853FC22B2CE", AppCategory.Bloatware)]
    // Games
    [InlineData("Microsoft.XboxApp", AppCategory.Games)]
    [InlineData("Microsoft.GamingApp", AppCategory.Games)]
    [InlineData("Microsoft.XboxGamingOverlay", AppCategory.Games)]
    [InlineData("Microsoft.Xbox.TCUI", AppCategory.Games)]
    [InlineData("Microsoft.MicrosoftSolitaireCollection", AppCategory.Games)]
    // Media
    [InlineData("Microsoft.Windows.Photos", AppCategory.Media)]         // not System despite Microsoft.Windows.*
    [InlineData("Microsoft.ZuneMusic", AppCategory.Media)]
    [InlineData("Microsoft.ZuneVideo", AppCategory.Media)]
    [InlineData("Microsoft.WindowsCamera", AppCategory.Media)]
    // Utilities
    [InlineData("Microsoft.WindowsNotepad", AppCategory.Utilities)]
    [InlineData("Microsoft.Paint", AppCategory.Utilities)]              // classic Paint, not Paint 3D
    [InlineData("Microsoft.ScreenSketch", AppCategory.Utilities)]
    [InlineData("Microsoft.WindowsTerminal", AppCategory.Utilities)]
    // System (frameworks, shell, codecs, GUID-named apps, protected)
    [InlineData("Microsoft.WindowsCalculator", AppCategory.System)]
    [InlineData("Microsoft.WindowsStore", AppCategory.System)]
    [InlineData("Microsoft.DesktopAppInstaller", AppCategory.System)]
    [InlineData("Microsoft.VCLibs.140.00.UWPDesktop", AppCategory.System)]
    [InlineData("Microsoft.UI.Xaml.2.8", AppCategory.System)]
    [InlineData("Microsoft.NET.Native.Framework.2.2", AppCategory.System)]
    [InlineData("Microsoft.HEIFImageExtension", AppCategory.System)]
    [InlineData("Microsoft.VP9VideoExtensions", AppCategory.System)]
    [InlineData("Microsoft.Windows.ShellExperienceHost", AppCategory.System)]
    [InlineData("windows.immersivecontrolpanel", AppCategory.System)]
    [InlineData("Microsoft.XboxGameCallableUI", AppCategory.System)]    // not Games despite "Xbox"
    [InlineData("c5e2524a-ea46-4f67-841f-6a9465d9d515", AppCategory.System)]
    [InlineData("Microsoft.MicrosoftEdge.Stable", AppCategory.System)]
    // Fallbacks
    [InlineData("Microsoft.WindowsCommunicationsApps", AppCategory.Other)]
    [InlineData("Microsoft.YourPhone", AppCategory.Other)]
    [InlineData("SomeVendor.CoolApp", AppCategory.ThirdParty)]
    [InlineData("", AppCategory.Other)]
    [InlineData("   ", AppCategory.Other)]
    [InlineData(null, AppCategory.Other)]
    public void Categorize_KnownPackages(string? name, AppCategory expected)
    {
        Assert.Equal(expected, UwpCatalog.Categorize(name));
    }

    [Theory]
    [InlineData("microsoft.bingnews")]
    [InlineData("MICROSOFT.BINGNEWS")]
    [InlineData("KING.COM.CANDYCRUSHSODASAGA")]
    public void Categorize_IsCaseInsensitive(string name)
    {
        Assert.Equal(AppCategory.Bloatware, UwpCatalog.Categorize(name));
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator", true)]
    [InlineData("Microsoft.WindowsStore", true)]
    [InlineData("Microsoft.StorePurchaseApp", true)]
    [InlineData("Microsoft.DesktopAppInstaller", true)]
    [InlineData("Microsoft.VCLibs.140.00", true)]
    [InlineData("Microsoft.SecHealthUI", true)]
    [InlineData("microsoft.windowscalculator", true)]
    [InlineData("Microsoft.BingNews", false)]
    [InlineData("Microsoft.Windows.Photos", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCritical_ProtectsTheRightPackages(string? name, bool expected)
    {
        Assert.Equal(expected, UwpCatalog.IsCritical(name));
    }

    [Theory]
    [InlineData("Microsoft.WindowsStore.CandyCrush")]   // would match a junk pattern too
    [InlineData("king.com.VCLibs")]
    [InlineData("Microsoft.Bing.UI.Xaml")]
    public void ProtectedPackage_IsNeverJunk_EvenIfItAlsoMatchesAJunkPattern(string name)
    {
        Assert.True(UwpCatalog.IsCritical(name));
        Assert.Equal(AppCategory.System, UwpCatalog.Categorize(name));
    }

    [Fact]
    public void FromIdentity_DerivesDisplayNameCategoryAndProtection()
    {
        var app = InstalledApp.FromIdentity("Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator_11.2_x64__8wekyb3d8bbwe", @"C:\Program Files\WindowsApps\calc");
        Assert.Equal("WindowsCalculator", app.DisplayName);
        Assert.True(app.IsCritical);
        Assert.Equal(AppCategory.System, app.Category);
        Assert.Equal(@"C:\Program Files\WindowsApps\calc", app.InstallLocation);
    }

    [Fact]
    public void SizeText_IsLocalized()
    {
        var app = new InstalledApp { SizeBytes = 3 * 1024 * 1024 / 2 }; // 1.5 MB
        using (new CultureScope("ru-RU")) Assert.Equal("1,5 МБ", app.SizeText);
        using (new CultureScope("en-US")) Assert.Equal("1.5 MB", app.SizeText);
        Assert.Equal("—", new InstalledApp().SizeText);
    }
}

public class UwpListingTests
{
    private const string SampleOutput =
        "Microsoft.WindowsCalculator|Microsoft.WindowsCalculator_11.2_x64__8wekyb3d8bbwe|C:\\WindowsApps\\calc\r\n" +
        "Microsoft.BingNews|Microsoft.BingNews_4.1_x64__8wekyb3d8bbwe|C:\\WindowsApps\\news\r\n" +
        "Microsoft.BingNews|Microsoft.BingNews_4.1_x64__8wekyb3d8bbwe|C:\\WindowsApps\\news\r\n" + // same package, 2nd user
        "garbage line without separators\r\n" +
        "|missing-name|x\r\n" +
        "Microsoft.XboxApp|Microsoft.XboxApp_48_x64__8wekyb3d8bbwe|\r\n" +                        // no install location
        "king.com.CandyCrushSaga|king.com.CandyCrushSaga_1.0_x86__kgqvnymyfvs32|C:\\WindowsApps\\candy\n" +
        "\r\n";

    [Fact]
    public void Parse_DeduplicatesSkipsMalformedAndSortsJunkFirstSystemLast()
    {
        var apps = UwpListing.Parse(SampleOutput);

        Assert.Equal(
            new[] { "Microsoft.BingNews", "king.com.CandyCrushSaga", "Microsoft.XboxApp", "Microsoft.WindowsCalculator" },
            apps.Select(a => a.Name));
        Assert.Equal(
            new[] { AppCategory.Bloatware, AppCategory.Bloatware, AppCategory.Games, AppCategory.System },
            apps.Select(a => a.Category));
    }

    [Fact]
    public void Parse_MapsFields()
    {
        var xbox = UwpListing.Parse(SampleOutput).Single(a => a.Name == "Microsoft.XboxApp");
        Assert.Equal("Microsoft.XboxApp_48_x64__8wekyb3d8bbwe", xbox.PackageFullName);
        Assert.Equal("", xbox.InstallLocation);
        Assert.False(xbox.IsCritical);
    }

    [Fact]
    public void Parse_ReadsVersionAndArchitectureFromFullName()
    {
        var candy = UwpListing.Parse(SampleOutput).Single(a => a.Name == "king.com.CandyCrushSaga");
        Assert.Equal("x86", candy.Architecture);
        Assert.Equal("1.0", candy.Version);
    }

    [Fact]
    public void Parse_SameNameTwoArchitectures_ShowsArchitecture()
    {
        // The field test showed "WindowsAppRuntime.1.8" twice: x64 and x86 builds.
        var apps = UwpListing.Parse(
            "Microsoft.WindowsAppRuntime.1.8|Microsoft.WindowsAppRuntime.1.8_8000.616.304.0_x64__8wekyb3d8bbwe|C:\\a\n" +
            "Microsoft.WindowsAppRuntime.1.8|Microsoft.WindowsAppRuntime.1.8_8000.616.304.0_x86__8wekyb3d8bbwe|C:\\b\n" +
            "Microsoft.BingNews|Microsoft.BingNews_4.1_x64__8wekyb3d8bbwe|C:\\c\n");

        Assert.Equal(
            new[] { "BingNews", "WindowsAppRuntime.1.8 (x64)", "WindowsAppRuntime.1.8 (x86)" },
            apps.Select(a => a.DisplayName));
    }

    [Fact]
    public void Parse_SameNameAndArchitecture_ShowsVersion()
    {
        var apps = UwpListing.Parse(
            "Microsoft.VCLibs.140.00|Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe|C:\\a\n" +
            "Microsoft.VCLibs.140.00|Microsoft.VCLibs.140.00_14.0.30704.0_x64__8wekyb3d8bbwe|C:\\b\n" +
            "Microsoft.VCLibs.140.00|Microsoft.VCLibs.140.00_14.0.33519.0_x86__8wekyb3d8bbwe|C:\\c\n");

        Assert.Equal(
            new[] { "VCLibs.140.00 (x64, 14.0.30704.0)", "VCLibs.140.00 (x64, 14.0.33519.0)", "VCLibs.140.00 (x86)" },
            apps.Select(a => a.DisplayName));
    }

    [Fact]
    public void FromIdentity_MalformedFullName_LeavesIdentityPartsEmpty()
    {
        var app = InstalledApp.FromIdentity("Contoso.App", "not-a-full-name", "");
        Assert.Equal("", app.Architecture);
        Assert.Equal("", app.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void Parse_EmptyOutput_ReturnsEmpty(string? output)
    {
        Assert.Empty(UwpListing.Parse(output));
    }
}
