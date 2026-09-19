using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using Observation.App.Services;
using Observation.Core.Scripts;
using Observation.Core.Tweaks;
using Observation.Handlers.Scripts;

namespace Observation.App.ViewModels;

/// <summary>
/// «Скрипты» — только разовые действия (см. ScriptItemViewModel), без накопительной очереди/
/// тумблеров. Встроенная библиотека (Scripts, из JSON-реестра) плюс пользовательские .ps1/.bat,
/// добавленные через файловый диалог (CustomScripts) — живут только в течение сессии, не
/// сохраняются на диск (см. план: список функций не требовал персистентности между запусками,
/// добавлять её не в этом проходе).
/// </summary>
public sealed class ScriptsTabViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public IReadOnlyList<ScriptItemViewModel> Scripts { get; }
    public ObservableCollection<ScriptItemViewModel> CustomScripts { get; } = new();

    private readonly PowerShellScriptRunner _runner;

    public RelayCommand AddCustomScriptCommand { get; }

    public ScriptsTabViewModel(ILocalizationService localization, IReadOnlyList<ScriptDefinition> scripts, PowerShellScriptRunner runner)
    {
        Localization = localization;
        _runner = runner;
        Scripts = scripts.Where(s => s.Tab == "scripts").Select(s => new ScriptItemViewModel(s, runner, localization)).ToList();

        AddCustomScriptCommand = new RelayCommand(AddCustomScript);
    }

    private void AddCustomScript()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PowerShell/Batch (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd|All files (*.*)|*.*",
            Title = "Observation"
        };
        if (dialog.ShowDialog() != true)
            return;

        var name = Path.GetFileName(dialog.FileName);
        var definition = new ScriptDefinition
        {
            Id = $"custom.{Guid.NewGuid()}",
            Tab = "scripts",
            Group = new LocalizedText { Ru = "Свои скрипты", En = "Custom scripts" },
            Name = new LocalizedText { Ru = name, En = name },
            Description = new LocalizedText { Ru = dialog.FileName, En = dialog.FileName },
            // Неизвестный пользовательский скрипт — по умолчанию Situational (может быть чем угодно,
            // от безобидного до опасного), не Safe: severity здесь — не оценка конкретного файла,
            // а честное «не знаем», в отличие от встроенной библиотеки с проверенными командами.
            Severity = Severity.Situational,
            FilePath = dialog.FileName,
            RequiresReboot = false
        };

        CustomScripts.Add(new ScriptItemViewModel(definition, _runner, Localization, onRemove: item => CustomScripts.Remove(item)));
    }
}
