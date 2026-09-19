using System.Diagnostics;
using Microsoft.Win32;
using Observation.Core.Handlers;
using Observation.Core.SystemAccess;
using Observation.Core.Tweaks;

namespace Observation.Handlers.OneDrive;

/// <summary>
/// Удаление OneDrive — best-effort (см. «Механизм отката — по типам» в плане, риск потери
/// данных для тех, кто пользуется синхронизацией). Команда деинсталляции и место остаточных
/// папок — из плана (OneDriveSetup.exe /uninstall из System32/SysWOW64 + очистка остатков).
/// Реестровый ключ для пункта навигации в проводнике подтверждён через открытые источники
/// (Microsoft Q&A, TenForums, WinTips) — HKCU\SOFTWARE\Classes\CLSID\{018D5C66-...}\System.IsPinnedToNameSpaceTree.
/// </summary>
public sealed class OneDriveRemovalHandler : ITweakHandler
{
    private const string NavPaneClsidPath = @"SOFTWARE\Classes\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}";
    private readonly IRegistryAccessor _registry;

    public OneDriveRemovalHandler(IRegistryAccessor registry) => _registry = registry;

    public async Task<HandlerApplyResult> ApplyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default)
    {
        if (!desiredOn)
        {
            return new HandlerApplyResult(false,
                "Удаление OneDrive — best-effort, автоматическая переустановка не поддерживается (см. план); используйте winget/Store для возврата");
        }

        var setupPath = FindOneDriveSetup();
        if (setupPath is null)
            return new HandlerApplyResult(false, "OneDriveSetup.exe не найден — возможно, OneDrive уже удалён");

        try
        {
            using var process = Process.Start(new ProcessStartInfo(setupPath, "/uninstall") { UseShellExecute = false });
            if (process is not null)
                await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return new HandlerApplyResult(false, $"Не удалось запустить OneDriveSetup.exe: {ex.Message}");
        }

        TryDeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive"));

        try
        {
            _registry.WriteValue(RegistryHive.CurrentUser, NavPaneClsidPath, "System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
        }
        catch (Exception)
        {
            // Best-effort: раздел может не существовать в этой версии OneDrive — не критично для основной операции.
        }

        return new HandlerApplyResult(true, "OneDrive удалён (best-effort — переустановка через winget/Store не гарантирует состояние 1:1)");
    }

    public Task<bool> VerifyAsync(TweakDefinition tweak, bool desiredOn, CancellationToken cancellationToken = default) =>
        Task.FromResult(desiredOn == !Directory.Exists(GetOneDriveInstallFolder()));

    public Task<HandlerApplyResult> RevertAsync(TweakDefinition tweak, string? capturedState, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HandlerApplyResult(false,
            "Удаление OneDrive — best-effort, автоматический откат не поддерживается (см. «Механизм отката — по типам»)"));

    private static string GetOneDriveInstallFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive");

    private static string? FindOneDriveSetup()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var candidate in new[]
                 {
                     Path.Combine(systemRoot, "SysWOW64", "OneDriveSetup.exe"),
                     Path.Combine(systemRoot, "System32", "OneDriveSetup.exe")
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort — файлы могут быть заняты/защищены, это не отказ всего твика.
        }
    }
}
