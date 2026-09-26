
namespace Stakeout.Services;

/// <summary>A registry value as captured at a point in time.</summary>
/// <param name="Value">The value (null when absent).</param>
/// <param name="Kind">Registry type of the value.</param>
/// <param name="Existed">False when the value (or its key) did not exist.</param>
public readonly record struct RegistryValueSnapshot(object? Value, RegValueKind Kind, bool Existed)
{
    public static RegistryValueSnapshot Absent { get; } = new(null, RegValueKind.Unknown, false);
}

/// <summary>A registry write with its desired post-apply value.</summary>
public sealed record RegistryOp(
    RegHive Hive, string SubKey, string Name, object Value, RegValueKind Kind);

/// <summary>
/// Minimal registry surface the rollback engine needs. The app implements it
/// over the real Windows registry; tests use an in-memory fake, which is what
/// lets the capture → persist → restore cycle be verified on any OS.
/// Implementations must not throw: failures are reported via return values.
/// </summary>
public interface IRegistryAccess
{
    RegistryValueSnapshot Capture(RegHive hive, string subKey, string name);
    bool SetValue(RegHive hive, string subKey, string name, object value, RegValueKind kind);
    /// <summary>Delete a value; a missing value counts as success.</summary>
    bool DeleteValue(RegHive hive, string subKey, string name);
}
