namespace Observation.Handlers.Apps;

/// <summary>Стартовый набор — расширяется по мере находок, как таблица конфликтов твиков.</summary>
public static class WingetCatalog
{
    public static IReadOnlyList<WingetCatalogEntry> BuiltIn { get; } = new[]
    {
        new WingetCatalogEntry("Browsers", "Google Chrome", "Google.Chrome"),
        new WingetCatalogEntry("Browsers", "Mozilla Firefox", "Mozilla.Firefox"),
        new WingetCatalogEntry("Development", "Visual Studio Code", "Microsoft.VisualStudioCode"),
        new WingetCatalogEntry("Development", "Git", "Git.Git"),
        new WingetCatalogEntry("Games", "Steam", "Valve.Steam"),
        new WingetCatalogEntry("Games", "Epic Games Launcher", "EpicGames.EpicGamesLauncher"),
        new WingetCatalogEntry("Office", "LibreOffice", "TheDocumentFoundation.LibreOffice"),
        new WingetCatalogEntry("Multimedia", "VLC media player", "VideoLAN.VLC"),
        new WingetCatalogEntry("Utilities", "7-Zip", "7zip.7zip"),
        new WingetCatalogEntry("Communication", "Discord", "Discord.Discord")
    };

    /// <summary>«Установка всех версий VC++ redist и .NET Desktop Runtime одной кнопкой» — из плана.</summary>
    public static IReadOnlyList<WingetCatalogEntry> RuntimeBundle { get; } = new[]
    {
        new WingetCatalogEntry("Runtimes", "VC++ Redist 2015-2022 (x64)", "Microsoft.VCRedist.2015+.x64"),
        new WingetCatalogEntry("Runtimes", "VC++ Redist 2015-2022 (x86)", "Microsoft.VCRedist.2015+.x86"),
        new WingetCatalogEntry("Runtimes", ".NET Desktop Runtime 8", "Microsoft.DotNet.DesktopRuntime.8")
    };
}
