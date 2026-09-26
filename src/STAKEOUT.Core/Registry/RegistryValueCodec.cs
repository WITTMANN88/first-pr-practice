using System.Text;

namespace Stakeout.Services;

/// <summary>
/// Encodes registry values to bytes (and back) so captured originals can be stored
/// as Base64 in the rollback JSON and restored exactly.
///
/// Encoding per kind:
///   DWord         4 bytes little-endian (int, so 0xFFFFFFFF round-trips as -1 like the registry API)
///   QWord         8 bytes little-endian (long)
///   String /
///   ExpandString  UTF-8
///   MultiString   count + length-prefixed UTF-8 items (unambiguous: [] ≠ [""])
///   Binary / None raw bytes
/// </summary>
public static class RegistryValueCodec
{
    public static byte[] Encode(object value, RegValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        return kind switch
        {
            RegValueKind.DWord => BitConverter.GetBytes(unchecked((int)Convert.ToInt64(value))),
            RegValueKind.QWord => BitConverter.GetBytes(Convert.ToInt64(value)),
            RegValueKind.String or RegValueKind.ExpandString
                => Encoding.UTF8.GetBytes(Convert.ToString(value) ?? string.Empty),
            RegValueKind.MultiString => EncodeMulti((string[])value),
            RegValueKind.Binary or RegValueKind.None => (byte[])((byte[])value).Clone(),
            _ => throw new NotSupportedException($"Registry value kind '{kind}' cannot be encoded."),
        };
    }

    public static object Decode(byte[] bytes, RegValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return kind switch
        {
            RegValueKind.DWord => BitConverter.ToInt32(RequireLength(bytes, 4, kind)),
            RegValueKind.QWord => BitConverter.ToInt64(RequireLength(bytes, 8, kind)),
            RegValueKind.String or RegValueKind.ExpandString => Encoding.UTF8.GetString(bytes),
            RegValueKind.MultiString => DecodeMulti(bytes),
            RegValueKind.Binary or RegValueKind.None => (byte[])bytes.Clone(),
            _ => throw new NotSupportedException($"Registry value kind '{kind}' cannot be decoded."),
        };
    }

    private static byte[] RequireLength(byte[] bytes, int length, RegValueKind kind)
        => bytes.Length == length
            ? bytes
            : throw new FormatException($"{kind} payload must be {length} bytes, got {bytes.Length}.");

    private static byte[] EncodeMulti(string[] items)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(items.Length);
            foreach (var item in items) w.Write(item ?? string.Empty);
        }
        return ms.ToArray();
    }

    private static string[] DecodeMulti(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms, Encoding.UTF8);
        var count = r.ReadInt32();
        if (count < 0 || count > bytes.Length) throw new FormatException("Corrupt MultiString payload.");
        var items = new string[count];
        for (var i = 0; i < count; i++) items[i] = r.ReadString();
        return items;
    }
}
