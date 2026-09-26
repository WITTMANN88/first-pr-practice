using Stakeout.Services;

namespace Stakeout.Tests.Uwp;

public class UwpLogoPickerTests
{
    private const string Base = "Square44x44Logo";
    private const string Png = ".png";

    [Fact]
    public void ExactFile_Wins()
    {
        Assert.Equal("Square44x44Logo.png", UwpLogoPicker.Pick(
            new[] { "Square44x44Logo.scale-200.png", "Square44x44Logo.png" }, Base, Png));
    }

    [Fact]
    public void PrefersUnplated32_ThenScale200()
    {
        var files = new[]
        {
            "Square44x44Logo.scale-100.png",
            "Square44x44Logo.targetsize-32.png",
            "Square44x44Logo.targetsize-32_altform-unplated.png",
            "Square44x44Logo.scale-200.png",
        };
        Assert.Equal("Square44x44Logo.targetsize-32_altform-unplated.png", UwpLogoPicker.Pick(files, Base, Png));
        Assert.Equal("Square44x44Logo.scale-200.png", UwpLogoPicker.Pick(files.Take(1).Append(files[3]), Base, Png));
    }

    [Fact]
    public void FindsVariantsInQualifierFolders()
    {
        // System packages keep logos in folders named after the qualifier.
        Assert.Equal(@"scale-200\Square44x44Logo.png", UwpLogoPicker.Pick(
            new[] { @"scale-100\Square44x44Logo.png", @"scale-200\Square44x44Logo.png" }, Base, Png));
    }

    [Fact]
    public void AvoidsHighContrastVariants_WhenOthersExist()
    {
        Assert.Equal("Square44x44Logo.targetsize-48.png", UwpLogoPicker.Pick(
            new[] { "Square44x44Logo.scale-200_contrast-black.png", "Square44x44Logo.targetsize-48.png" }, Base, Png));
    }

    [Fact]
    public void UnrelatedFiles_GiveNull()
    {
        Assert.Null(UwpLogoPicker.Pick(
            new[] { "Square44x44LogoOld.png", "StoreLogo.png", "Square44x44Logo.jpg", "Square44x44Logo..png" }, Base, Png));
    }

    [Theory]
    [InlineData("Square44x44Logo.png", true)]
    [InlineData("square44x44logo.SCALE-200.PNG", true)]
    [InlineData(@"contrast-white\scale-100\Square44x44Logo.png", true)]
    [InlineData("Square44x44LogoX.png", false)]
    [InlineData("Square44x44Logo.png.bak", false)]
    public void IsVariant(string path, bool expected)
    {
        Assert.Equal(expected, UwpLogoPicker.IsVariant(path, Base, Png));
    }
}
