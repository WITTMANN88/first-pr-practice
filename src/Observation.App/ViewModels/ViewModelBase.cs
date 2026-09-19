using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Observation.App.ViewModels;

/// <summary>
/// Минимальная база INotifyPropertyChanged без внешних MVVM-библиотек — план не решал
/// подключать сторонний MVVM-тулкит, поэтому обходимся вручную.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
