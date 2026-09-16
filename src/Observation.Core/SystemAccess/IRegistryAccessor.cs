using Microsoft.Win32;

namespace Observation.Core.SystemAccess;

/// <summary>
/// Абстракция над Microsoft.Win32.Registry — TweakEngine работает только через неё,
/// поэтому в Observation.Tests можно подставить мок-реализацию без реальных изменений
/// системы (см. «Observation.Tests» в «Архитектура кода»).
/// </summary>
public interface IRegistryAccessor
{
    bool TryReadValue(RegistryHive hive, string path, string valueName, out object? data, out RegistryValueKind kind);
    void WriteValue(RegistryHive hive, string path, string valueName, object data, RegistryValueKind kind);
    void DeleteValue(RegistryHive hive, string path, string valueName);
}
