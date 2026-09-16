using System.ComponentModel;
using Observation.App.Services;
using Xunit;

namespace Observation.App.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void Indexer_DefaultsToRussian()
    {
        var localization = new LocalizationService();

        Assert.Equal("Главная", localization["NavHome"]);
    }

    [Fact]
    public void SetLanguage_SwitchesToEnglish()
    {
        var localization = new LocalizationService();

        localization.SetLanguage("en");

        Assert.Equal("Home", localization["NavHome"]);
        Assert.Equal("en", localization.CurrentLanguage);
    }

    [Fact]
    public void Indexer_ReturnsKeyItself_WhenUnknown()
    {
        var localization = new LocalizationService();

        Assert.Equal("NoSuchKey", localization["NoSuchKey"]);
    }

    [Fact]
    public void SetLanguage_RaisesPropertyChanged_ForCurrentLanguageAndIndexer()
    {
        var localization = new LocalizationService();
        var raised = new List<string?>();
        localization.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        localization.SetLanguage("en");

        Assert.Contains(nameof(ILocalizationService.CurrentLanguage), raised);
        Assert.Contains("Item[]", raised);
    }

    [Fact]
    public void SetLanguage_IsNoOp_WhenAlreadyCurrentLanguage()
    {
        var localization = new LocalizationService();
        var raised = 0;
        localization.PropertyChanged += (_, _) => raised++;

        localization.SetLanguage("ru");

        Assert.Equal(0, raised);
    }
}
