using System.Globalization;
using System.Text;

namespace Stakeout.Services;

/// <summary>
/// Builds the "what exactly changes" text of a tweak's help tooltip: registry
/// values grouped under their key, one per line, in the order they are written.
/// Language-neutral (paths, names, numbers, commands), so it needs no translation.
/// </summary>
public static class TweakDetails
{
    private const string Indent = "  ";

    /// <summary>Registry writes as "HKLM\path" followed by indented "Name = value" lines.</summary>
    public static string Registry(IEnumerable<RegistryOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var sb = new StringBuilder();
        string? currentKey = null;
        foreach (var op in ops)
        {
            var key = KeyPath(op.Hive, op.SubKey);
            if (!string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(key);
                currentKey = key;
            }
            sb.Append('\n').Append(Indent).Append(op.Name).Append(" = ").Append(FormatValue(op.Value, op.Kind));
        }
        return sb.ToString();
    }

    /// <summary>A key followed by indented lines, for changes that are not single values (e.g. a list of entries).</summary>
    public static string Key(RegHive hive, string subKey, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sb = new StringBuilder(KeyPath(hive, subKey));
        foreach (var line in lines) sb.Append('\n').Append(Indent).Append(line);
        return sb.ToString();
    }

    /// <summary>Join sections (registry blocks, commands) with blank lines between them.</summary>
    public static string Join(params string[] sections)
        => string.Join("\n\n", sections.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>"HKLM\SOFTWARE\…" with the conventional short hive name.</summary>
    public static string KeyPath(RegHive hive, string subKey) => $@"{HiveName(hive)}\{subKey}";

    public static string HiveName(RegHive hive) => hive switch
    {
        RegHive.LocalMachine => "HKLM",
        RegHive.CurrentUser => "HKCU",
        RegHive.Users => "HKU",
        RegHive.ClassesRoot => "HKCR",
        RegHive.CurrentConfig => "HKCC",
        _ => hive.ToString(),
    };

    /// <summary>
    /// DWORD/QWORD as unsigned numbers (hex when it reads better, e.g. 0xFFFFFFFF),
    /// strings quoted so "0" and 0 stay distinguishable.
    /// </summary>
    public static string FormatValue(object value, RegValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        return kind switch
        {
            RegValueKind.DWord => Unsigned(unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)), 8),
            RegValueKind.QWord => Unsigned(unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)), 16),
            RegValueKind.String or RegValueKind.ExpandString => $"\"{value}\"",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
    }

    private static string Unsigned(ulong value, int hexDigits) => value > 0xFFFF
        ? "0x" + value.ToString("X" + hexDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
        : value.ToString(CultureInfo.InvariantCulture);
}
