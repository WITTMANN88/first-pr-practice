using System.Diagnostics.CodeAnalysis;
using Stakeout.Models;

namespace Stakeout.Services;

/// <summary>A <see cref="SavedValue"/> decoded back into registry terms.</summary>
public readonly record struct DecodedRegistryValue(
    RegHive Hive, string SubKey, string Name, bool Existed, object? Value, RegValueKind Kind);

/// <summary>
/// Maps between live registry snapshots and the serializable <see cref="SavedValue"/>
/// records stored in tweak-state.json.
/// </summary>
public static class SavedValueCodec
{
    private static readonly Dictionary<RegHive, string> HiveNames = new()
    {
        [RegHive.LocalMachine] = "HKLM",
        [RegHive.CurrentUser] = "HKCU",
        [RegHive.Users] = "HKU",
        [RegHive.ClassesRoot] = "HKCR",
        [RegHive.CurrentConfig] = "HKCC",
    };

    private static readonly Dictionary<string, RegHive> HivesByName =
        HiveNames.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static string HiveToString(RegHive hive)
        => HiveNames.TryGetValue(hive, out var s)
            ? s
            : throw new ArgumentOutOfRangeException(nameof(hive), hive, "Unsupported registry hive.");

    public static SavedValue ToSaved(RegHive hive, string subKey, string name, RegistryValueSnapshot snap)
    {
        string? encoded = null;
        if (snap.Existed && snap.Value != null)
            encoded = Convert.ToBase64String(RegistryValueCodec.Encode(snap.Value, snap.Kind));

        return new SavedValue
        {
            Hive = HiveToString(hive),
            SubKey = subKey,
            Name = name,
            Existed = snap.Existed && snap.Value != null,
            Kind = snap.Existed ? snap.Kind.ToString() : RegValueKind.Unknown.ToString(),
            ValueBase64 = encoded,
        };
    }

    /// <summary>
    /// Decode a stored record. Returns false (never throws) for records that cannot
    /// be trusted — unknown hive, unknown kind, or a corrupt payload — so a damaged
    /// entry is skipped rather than written back as the wrong type.
    /// </summary>
    public static bool TryDecode(SavedValue sv, [NotNullWhen(true)] out DecodedRegistryValue? decoded)
    {
        decoded = null;
        if (sv is null || string.IsNullOrEmpty(sv.SubKey) || sv.Name is null) return false;
        if (!HivesByName.TryGetValue(sv.Hive ?? "", out var hive)) return false;

        if (!sv.Existed)
        {
            decoded = new DecodedRegistryValue(hive, sv.SubKey, sv.Name, false, null, RegValueKind.Unknown);
            return true;
        }

        if (sv.ValueBase64 is null) return false;
        if (!Enum.TryParse<RegValueKind>(sv.Kind, ignoreCase: false, out var kind) ||
            !Enum.IsDefined(kind) || kind == RegValueKind.Unknown)
        {
            return false;
        }

        try
        {
            var value = RegistryValueCodec.Decode(Convert.FromBase64String(sv.ValueBase64), kind);
            decoded = new DecodedRegistryValue(hive, sv.SubKey, sv.Name, true, value, kind);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or EndOfStreamException
                                       or ArgumentException)
        {
            return false;
        }
    }
}
