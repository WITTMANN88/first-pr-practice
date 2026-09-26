namespace Stakeout.Services;

// Core-owned mirrors of Microsoft.Win32.RegistryHive / RegistryValueKind.
// The platform analyzer marks the Win32 types (even the enums) as Windows-only,
// so the OS-independent core cannot use them without suppressing CA1416. Names
// and numeric values are identical, so the Windows adapter in the app converts
// with a plain cast, and names serialize the same way ("HKLM", "DWord") in
// tweak-state.json. A unit test pins the parity.

/// <summary>Registry root key. Same names and values as Microsoft.Win32.RegistryHive.</summary>
public enum RegHive
{
    ClassesRoot = int.MinValue,
    CurrentUser,
    LocalMachine,
    Users,
    PerformanceData,
    CurrentConfig,
}

/// <summary>Registry value type. Same names and values as Microsoft.Win32.RegistryValueKind.</summary>
public enum RegValueKind
{
    None = -1,
    Unknown = 0,
    String = 1,
    ExpandString = 2,
    Binary = 3,
    DWord = 4,
    MultiString = 7,
    QWord = 11,
}
