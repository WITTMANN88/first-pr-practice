using Observation.Core.SystemAccess;
using Observation.Handlers.Debloat;
using Observation.Tests.Fakes;
using Xunit;

namespace Observation.Tests;

public class PowerShellUwpPackageScannerTests
{
    [Fact]
    public void ParsePackages_ReturnsEmptyList_ForEmptyOutput()
    {
        Assert.Empty(PowerShellUwpPackageScanner.ParsePackages(""));
        Assert.Empty(PowerShellUwpPackageScanner.ParsePackages("   "));
    }

    [Fact]
    public void ParsePackages_HandlesSingleObject_NotWrappedInArray()
    {
        // Windows PowerShell 5.1 ConvertTo-Json quirk — один результат сериализуется без [ ].
        const string json = "{\"Name\":\"Microsoft.ZuneMusic\",\"PackageFullName\":\"Microsoft.ZuneMusic_1.0_x64__8wekyb3d8bbwe\",\"Publisher\":\"CN=Microsoft\"}";

        var result = PowerShellUwpPackageScanner.ParsePackages(json);

        Assert.Single(result);
        Assert.Equal("Microsoft.ZuneMusic", result[0].Name);
    }

    [Fact]
    public void ParsePackages_HandlesArray_ForMultipleResults()
    {
        const string json = "[" +
            "{\"Name\":\"Microsoft.ZuneMusic\",\"PackageFullName\":\"Microsoft.ZuneMusic_1.0_x64__8wekyb3d8bbwe\",\"Publisher\":\"CN=Microsoft\"}," +
            "{\"Name\":\"king.com.CandyCrushSaga\",\"PackageFullName\":\"king.com.CandyCrushSaga_1.0_x64__abc\",\"Publisher\":\"CN=King\"}" +
            "]";

        var result = PowerShellUwpPackageScanner.ParsePackages(json);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParsePackages_FiltersOutAlwaysHiddenPackages()
    {
        const string json = "[" +
            "{\"Name\":\"Microsoft.DesktopAppInstaller\",\"PackageFullName\":\"Microsoft.DesktopAppInstaller_1.0_x64__8wekyb3d8bbwe\",\"Publisher\":\"CN=Microsoft\"}," +
            "{\"Name\":\"king.com.CandyCrushSaga\",\"PackageFullName\":\"king.com.CandyCrushSaga_1.0_x64__abc\",\"Publisher\":\"CN=King\"}" +
            "]";

        var result = PowerShellUwpPackageScanner.ParsePackages(json);

        Assert.Single(result);
        Assert.Equal("king.com.CandyCrushSaga", result[0].Name);
    }

    [Fact]
    public async Task ScanAsync_RunsGetAppxPackage_ExcludingFrameworksAndNonRemovable()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new CommandResult(0, "[]", "") };
        var scanner = new PowerShellUwpPackageScanner(runner);

        await scanner.ScanAsync();

        var script = string.Join(" ", runner.Calls[0].Arguments);
        Assert.Contains("Get-AppxPackage", script);
        Assert.Contains("IsFramework", script);
        Assert.Contains("NonRemovable", script);
    }

    [Fact]
    public async Task ScanAsync_Throws_WhenCommandFails()
    {
        var runner = new FakeCommandRunner { Handler = (_, _) => new CommandResult(1, "", "Access denied") };
        var scanner = new PowerShellUwpPackageScanner(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync());
    }

    [Fact]
    public async Task RemoveAsync_PassesPackageFullNameAndAllUsers()
    {
        var runner = new FakeCommandRunner();
        var scanner = new PowerShellUwpPackageScanner(runner);

        await scanner.RemoveAsync("king.com.CandyCrushSaga_1.0_x64__abc");

        var script = string.Join(" ", runner.Calls[0].Arguments);
        Assert.Contains("Remove-AppxPackage", script);
        Assert.Contains("king.com.CandyCrushSaga_1.0_x64__abc", script);
        Assert.Contains("-AllUsers", script);
    }
}
