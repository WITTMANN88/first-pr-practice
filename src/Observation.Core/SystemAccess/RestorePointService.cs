namespace Observation.Core.SystemAccess;

/// <summary>Created=false не всегда означает сбой — см. комментарий в TryCreateAsync про суточный лимит Windows.</summary>
public sealed record RestorePointResult(bool Created, string Message);

/// <summary>
/// Точка восстановления перед применением пакета твиков — см. «Точка восстановления»
/// в плане (вкладка 1 — Главная). <c>Checkpoint-Computer</c> — тот же командлет, что и
/// стандартное «Создать точку восстановления» в Windows.
/// </summary>
public sealed class RestorePointService
{
    private readonly ICommandRunner _runner;

    public RestorePointService(ICommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<RestorePointResult> TryCreateAsync(string description, CancellationToken cancellationToken = default)
    {
        var script =
            "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue; " +
            $"Checkpoint-Computer -Description \"{description}\" -RestorePointType MODIFY_SETTINGS -ErrorAction Stop";

        var result = await _runner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
            return new RestorePointResult(true, "Точка восстановления создана");

        // Windows ограничивает автоматические точки восстановления одной в сутки
        // (SystemRestorePointCreationFrequency, по умолчанию 1440 минут — см. аудит в плане).
        // Если точка уже создавалась сегодня, Checkpoint-Computer не создаёт новую — это
        // не сбой самого механизма, сообщаем честно, а не как ошибку.
        var reason = FirstNonEmptyLine(result.StandardError, result.StandardOutput);
        return new RestorePointResult(false,
            $"Точка восстановления не создана: {reason} (Windows позволяет не чаще одной в сутки — возможно, уже создавалась сегодня)");
    }

    private static string FirstNonEmptyLine(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length > 0)
                return trimmed.Split('\n')[0];
        }

        return "неизвестная причина";
    }
}
