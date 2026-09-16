using Microsoft.Win32;
using Observation.Core.SystemAccess;

namespace Observation.Tests.Fakes;

/// <summary>Мок-реестр в памяти — никаких реальных изменений системы, см. «Observation.Tests» в плане.</summary>
public sealed class FakeRegistryAccessor : IRegistryAccessor
{
    private sealed record Key(RegistryHive Hive, string Path, string ValueName);
    private sealed record Entry(object Data, RegistryValueKind Kind);

    private readonly Dictionary<Key, Entry> _values = new();

    public bool ThrowOnRead { get; set; }

    public void Seed(RegistryHive hive, string path, string valueName, object data, RegistryValueKind kind) =>
        _values[new Key(hive, path, valueName)] = new Entry(data, kind);

    public bool TryReadValue(RegistryHive hive, string path, string valueName, out object? data, out RegistryValueKind kind)
    {
        if (ThrowOnRead)
            throw new InvalidOperationException("Симулированный отказ чтения реестра");

        if (_values.TryGetValue(new Key(hive, path, valueName), out var entry))
        {
            data = entry.Data;
            kind = entry.Kind;
            return true;
        }

        data = null;
        kind = RegistryValueKind.Unknown;
        return false;
    }

    public void WriteValue(RegistryHive hive, string path, string valueName, object data, RegistryValueKind kind) =>
        _values[new Key(hive, path, valueName)] = new Entry(data, kind);

    public void DeleteValue(RegistryHive hive, string path, string valueName) =>
        _values.Remove(new Key(hive, path, valueName));
}
