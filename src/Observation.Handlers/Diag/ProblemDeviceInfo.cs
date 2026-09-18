namespace Observation.Handlers.Diag;

/// <summary>Одно устройство с ошибкой в диспетчере устройств (вкладка «Диагностика», список проблемных устройств).</summary>
public sealed record ProblemDeviceInfo(string Name, int ErrorCode);
