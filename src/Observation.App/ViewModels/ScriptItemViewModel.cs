using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Observation.App.Services;
using Observation.Core.Scripts;
using Observation.Core.Tweaks;
using Observation.Handlers.Scripts;

namespace Observation.App.ViewModels;

/// <summary>
/// Один пункт вкладки «Скрипты»/раздела «Очистка» — разовое действие с подтверждением
/// перед запуском (MessageBox, тот же паттерн, что и у конфликтов/crash-recovery) и
/// построчным логом вывода, не тумблер (см. ScriptDefinition).
/// </summary>
public sealed class ScriptItemViewModel : ViewModelBase
{
    private readonly PowerShellScriptRunner _runner;
    private readonly ILocalizationService _localization;

    public ScriptDefinition Definition { get; }
    public string Name => Definition.Name.Get(_localization.CurrentLanguage);
    public string Description => Definition.Description.Get(_localization.CurrentLanguage);

    public string SeverityLabel => _localization[Definition.Severity switch
    {
        Severity.Safe => "SeveritySafe",
        Severity.Situational => "SeveritySituational",
        _ => "SeverityRisky"
    }];

    public ObservableCollection<string> Log { get; } = new();

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    public RelayCommand RunCommand { get; }

    public ScriptItemViewModel(ScriptDefinition definition, PowerShellScriptRunner runner, ILocalizationService localization)
    {
        Definition = definition;
        _runner = runner;
        _localization = localization;
        _localization.PropertyChanged += OnLocalizationChanged;

        RunCommand = new RelayCommand(async () => await RunAsync(), () => !IsRunning);
    }

    private async Task RunAsync()
    {
        var proceed = MessageBox.Show(
            $"Выполнить «{Name}»?\n\n{Description}",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (proceed != MessageBoxResult.Yes)
            return;

        IsRunning = true;
        RunCommand.NotifyCanExecuteChanged();
        Log.Clear();

        try
        {
            // PowerShellScriptRunner's OutputDataReceived/ErrorDataReceived fire on a
            // ThreadPool thread — Log is a UI-bound ObservableCollection, so writes must be
            // marshaled back to the Dispatcher or WPF throws NotSupportedException and crashes
            // the process (confirmed on a real machine: the throw happens synchronously inside
            // the event handler, not inside the awaited Task, so it bypasses the catch below).
            var exitCode = await _runner.RunAsync(Definition.ScriptText,
                line => Application.Current.Dispatcher.Invoke(() => Log.Add(line)));
            Log.Add(exitCode == 0 ? "— завершено успешно —" : $"— завершено с кодом {exitCode} —");
        }
        catch (Exception ex)
        {
            Log.Add($"— ошибка: {ex.Message} —");
        }
        finally
        {
            IsRunning = false;
            RunCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ILocalizationService.CurrentLanguage) or "Item[]")
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(SeverityLabel));
        }
    }
}
