using System.Text;
using Microsoft.Win32;

namespace Stakeout.Services;

/// <summary>
/// Encodes/decodes registry values to a byte buffer so captured originals can
/// be stored as Base64 in JSON and round-tripped exactly on rollback. Covers the
/// value kinds this tool actually writes: DWord, QWord, String, ExpandString,
/// MultiString and Binary.
/// </summary>
internal static class RegistryValueCodec
{
    public static byte[] Encode(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => BitConverter.GetBytes(Convert.ToInt32(value)),
        RegistryValueKind.QWord => BitConverter.GetBytes(Convert.ToInt64(value)),
        RegistryValueKind.Binary => (byte[])value,
        RegistryValueKind.MultiString =>
            Encoding.UTF8.GetBytes(string.Join("\0", (string[])value)),
        _ => Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty),
    };

    public static object Decode(byte[] bytes, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => BitConverter.ToInt32(bytes, 0),
        RegistryValueKind.QWord => BitConverter.ToInt64(bytes, 0),
        RegistryValueKind.Binary => bytes,
        RegistryValueKind.MultiString => Encoding.UTF8.GetString(bytes).Split('\0'),
        _ => Encoding.UTF8.GetString(bytes),
    };
}
