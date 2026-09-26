using Stakeout.Models;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Uwp;

public class UwpCatalogTests
{
    [Theory]
    // Preinstalled / sponsored junk
    [InlineData("Microsoft.BingNews", UwpCategory.Bloatware)]
    [InlineData("Microsoft.BingWeather", UwpCategory.Bloatware)]
    [InlineData("Microsoft.GetHelp", UwpCategory.Bloatware)]
    [InlineData("Microsoft.Getstarted", UwpCategory.Bloatware)]
    [InlineData("Microsoft.MicrosoftOfficeHub", UwpCategory.Bloatware)]
    [InlineData("Microsoft.WindowsFeedbackHub", UwpCategory.Bloatware)]
    [InlineData("Microsoft.MSPaint", UwpCategory.Bloatware)]           // Paint 3D
    [InlineData("Microsoft.Windows.DevHome", UwpCategory.Bloatware)]   // beats the generic Microsoft.Windows.* rule
    [InlineData("MicrosoftWindows.Client.WebExperience", UwpCategory.Bloatware)]
    [InlineData("Clipchamp.Clipchamp", UwpCategory.Bloatware)]
    [InlineData("king.com.CandyCrushSaga", UwpCategory.Bloatware)]
    [InlineData("SpotifyAB.SpotifyMusic", UwpCategory.Bloatware)]
    [InlineData("4DF9E0F8.Netflix", UwpCategory.Bloatware)]
    [InlineData("Disney.37853FC22B2CE", UwpCategory.Bloatware)]
    // Games
    [InlineData("Microsoft.XboxApp", UwpCategory.Games)]
    [InlineData("Microsoft.GamingApp", UwpCategory.Games)]
    [InlineData("Microsoft.XboxGamingOverlay", UwpCategory.Games)]
    [InlineData("Microsoft.Xbox.TCUI", UwpCategory.Games)]
    [InlineData("Microsoft.MicrosoftSolitaireCollection", UwpCategory.Games)]
    // Media
    [InlineData("Microsoft.Windows.Photos", UwpCategory.Media)]         // not System despite Microsoft.Windows.*
    [InlineData("Microsoft.ZuneMusic", UwpCategory.Media)]
    [InlineData("Microsoft.ZuneVideo", UwpCategory.Media)]
    [InlineData("Microsoft.WindowsCamera", UwpCategory.Media)]
    // Utilities
    [InlineData("Microsoft.WindowsNotepad", UwpCategory.Utilities)]
    [InlineData("Microsoft.Paint", UwpCategory.Utilities)]              // classic Paint, not Paint 3D
    [InlineData("Microsoft.ScreenSketch", UwpCategory.Utilities)]
    [InlineData("Microsoft.WindowsTerminal", UwpCategory.Utilities)]
    // System (frameworks, shell, codecs, GUID-named apps, protected)
    [InlineData("Microsoft.WindowsCalculator", UwpCategory.System)]
    [InlineData("Microsoft.WindowsStore", UwpCategory.System)]
    [InlineData("Microsoft.DesktopAppInstaller", UwpCategory.System)]
    [InlineData("Microsoft.VCLibs.140.00.UWPDesktop", UwpCategory.System)]
    [InlineData("Microsoft.UI.Xaml.2.8", UwpCategory.System)]
    [InlineData("Microsoft.NET.Native.Framework.2.2", UwpCategory.System)]
    [InlineData("Microsoft.HEIFImageExtension", UwpCategory.System)]
    [InlineData("Microsoft.VP9VideoExtensions", UwpCategory.System)]
    [InlineData("Microsoft.Windows.ShellExperienceHost", UwpCategory.System)]
    [InlineData("windows.immersivecontrolpanel", UwpCategory.System)]
    [InlineData("Microsoft.XboxGameCallableUI", UwpCategory.System)]    // not Games despite "Xbox"
    [InlineData("c5e2524a-ea46-4f67-841f-6a9465d9d515", UwpCategory.System)]
    [InlineData("Microsoft.MicrosoftEdge.Stable", UwpCategory.System)]
    // Fallbacks
    [InlineData("Microsoft.WindowsCommunicationsApps", UwpCategory.Other)]
    [InlineData("Microsoft.YourPhone", UwpCategory.Other)]
    [InlineData("SomeVendor.CoolApp", UwpCategory.ThirdParty)]
    [InlineData("", UwpCategory.Other)]
    [InlineData("   ", UwpCategory.Other)]
    [InlineData(null, UwpCategory.Other)]
    public void Categorize_KnownPackages(string? name, UwpCategory expected)
    {
        Assert.Equal(expected, UwpCatalog.Categorize(name));
    }

    [Theory]
    [InlineData("microsoft.bingnews")]
    [InlineData("MICROSOFT.BINGNEWS")]
    [InlineData("KING.COM.CANDYCRUSHSODASAGA")]
    public void Categorize_IsCaseInsensitive(string name)
    {
        Assert.Equal(UwpCategory.Bloatware, UwpCatalog.Categorize(name));
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
        Assert.Equal(UwpCategory.System, UwpCatalog.Categorize(name));
    }

    [Fact]
    public void FromIdentity_DerivesDisplayNameCategoryAndProtection()
    {
        var app = UwpApp.FromIdentity("Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator_11.2_x64__8wekyb3d8bbwe", @"C:\Program Files\WindowsApps\calc");
        Assert.Equal("WindowsCalculator", app.DisplayName);
        Assert.True(app.IsCritical);
        Assert.Equal(UwpCategory.System, app.Category);
        Assert.Equal(@"C:\Program Files\WindowsApps\calc", app.InstallLocation);
    }

    [Fact]
    public void SizeText_IsLocalized()
    {
        var app = new UwpApp { SizeBytes = 3 * 1024 * 1024 / 2 }; // 1.5 MB
        using (new CultureScope("ru-RU")) Assert.Equal("1,5 МБ", app.SizeText);
        using (new CultureScope("en-US")) Assert.Equal("1.5 MB", app.SizeText);
        Assert.Equal("—", new UwpApp().SizeText);
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
            new[] { UwpCategory.Bloatware, UwpCategory.Bloatware, UwpCategory.Games, UwpCategory.System },
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
        var app = UwpApp.FromIdentity("Contoso.App", "not-a-full-name", "");
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
