namespace Stakeout.Core;

/// <summary>
/// Small helper that caps how long the UI will wait on a background operation.
/// The underlying work may keep running, but the awaiting view-model gets control
/// back so <c>IsLoading</c> is always cleared and the UI never appears frozen.
/// </summary>
public static class TimeoutGuard
{
    /// <summary>
    /// Await <paramref name="task"/> but give up after <paramref name="timeout"/>,
    /// returning <paramref name="fallback"/> and logging a TIMEOUT on expiry.
    /// </summary>
    public static async Task<T> Await<T>(Task<T> task, TimeSpan timeout, T fallback, string label)
    {
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Logger.Log(label, "TIMEOUT", $"exceeded {timeout.TotalSeconds:0}s; UI released");
            return fallback;
        }
        catch (Exception ex)
        {
            Logger.LogError(label, ex);
            return fallback;
        }
    }
}
