using System.ComponentModel;
using System.Reflection;
using Observation.App.Services;
using Xunit;

namespace Observation.App.Tests;

public class LocalizationServiceTests
{
    /// <summary>
    /// Регрессия: HomeActiveTweaksLabel/HomeVerifyBtn/HomeRevertBtn были добавлены в Ru,
    /// но забыты в En — индексатор на En просто возвращал сырой ключ вместо перевода
    /// без единой ошибки компиляции/теста, пока это не поймали на реальной машине. Сверяем
    /// оба словаря через рефлексию, раз они private static — иначе тест не увидел бы новое расхождение.
    /// </summary>
    [Fact]
    public void RuAndEn_HaveIdenticalKeySets()
    {
        var ru = (Dictionary<string, string>)typeof(LocalizationService)
            .GetField("Ru", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var en = (Dictionary<string, string>)typeof(LocalizationService)
            .GetField("En", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        var missingFromEn = ru.Keys.Except(en.Keys).ToList();
        var missingFromRu = en.Keys.Except(ru.Keys).ToList();

        Assert.True(missingFromEn.Count == 0, $"Ключи есть в Ru, но нет в En: {string.Join(", ", missingFromEn)}");
        Assert.True(missingFromRu.Count == 0, $"Ключи есть в En, но нет в Ru: {string.Join(", ", missingFromRu)}");
    }

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
