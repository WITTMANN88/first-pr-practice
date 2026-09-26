namespace Stakeout.Core;

/// <summary>
/// Modal questions a view model needs answered (e.g. "disable BitLocker?").
/// Keeps MessageBox and other UI types out of view models so they stay testable;
/// the app provides the WPF implementation.
/// </summary>
public interface IDialogService
{
    /// <summary>Ask a yes/no question about a potentially destructive action.</summary>
    bool Confirm(string title, string message);
}
