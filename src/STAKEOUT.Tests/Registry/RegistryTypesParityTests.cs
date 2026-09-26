using System.Globalization;
using Stakeout.Services;

namespace Stakeout.Tests.Registry;

// The app converts RegHive/RegValueKind to the Win32 enums with a plain cast.
// These tests prove that is safe: same member names, same numeric values.
// Reflecting over the Win32 enums is platform-neutral (the types ship in the
// shared framework on every OS), so the Windows-only analyzer warning is
// suppressed for this file only.
#pragma warning disable CA1416

public class RegistryTypesParityTests
{
    [Fact]
    public void RegHive_MatchesMicrosoftWin32RegistryHive()
        => AssertSameEnum<RegHive, Microsoft.Win32.RegistryHive>();

    [Fact]
    public void RegValueKind_MatchesMicrosoftWin32RegistryValueKind()
        => AssertSameEnum<RegValueKind, Microsoft.Win32.RegistryValueKind>();

    private static void AssertSameEnum<TOurs, TWin32>()
        where TOurs : struct, Enum
        where TWin32 : struct, Enum
    {
        var ours = Enum.GetValues<TOurs>().ToDictionary(v => v.ToString(), v => Convert.ToInt64(v, CultureInfo.InvariantCulture));
        var win32 = Enum.GetValues<TWin32>().ToDictionary(v => v.ToString(), v => Convert.ToInt64(v, CultureInfo.InvariantCulture));
        Assert.Equal(win32.OrderBy(kv => kv.Key), ours.OrderBy(kv => kv.Key));
    }
}
