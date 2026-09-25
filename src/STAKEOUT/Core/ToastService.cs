namespace Stakeout.Core;

public enum ToastKind { Success, Error, Info }

public sealed class ToastMessage
{
    public string Text { get; init; } = "";
    public ToastKind Kind { get; init; } = ToastKind.Info;
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>
/// App-wide toast broker. View models raise toasts; the shell listens and shows
/// them in the bottom-right corner for 5 seconds. Decouples pages from the shell.
/// </summary>
public sealed class ToastService
{
    public event Action<ToastMessage>? Raised;

    public void Show(string text, ToastKind kind = ToastKind.Info)
    {
        Logger.Log("Toast", kind.ToString().ToUpperInvariant(), text);
        Raised?.Invoke(new ToastMessage { Text = text, Kind = kind });
    }

    public void Success(string text) => Show(text, ToastKind.Success);
    public void Error(string text) => Show(text, ToastKind.Error);
}
