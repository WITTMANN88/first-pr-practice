namespace Observation.Core.Tweaks;

/// <summary>Название/описание твика на русском и английском — язык переключается без перезапуска (ILocalizationService).</summary>
public sealed class LocalizedText
{
    public required string Ru { get; init; }
    public required string En { get; init; }

    public string Get(string languageCode) =>
        languageCode.Equals("en", StringComparison.OrdinalIgnoreCase) ? En : Ru;
}
