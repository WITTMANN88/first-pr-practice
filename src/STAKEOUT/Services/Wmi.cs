using System.Management;

namespace Stakeout.Services;

/// <summary>
/// WMI enumeration with deterministic cleanup. The collection returned by
/// <c>ManagementObjectSearcher.Get()</c> and every object it yields wrap COM
/// interfaces (IEnumWbemClassObject / IWbemClassObject). Left to the finalizer,
/// that native memory stays allocated until some later GC (and the finalizer
/// thread must marshal back to COM), so every query disposes both.
/// </summary>
public static class Wmi
{
    /// <summary>Semisynchronous enumeration: required for the timeout to apply.</summary>
    public static EnumerationOptions Options(TimeSpan timeout) => new()
    {
        Timeout = timeout,
        ReturnImmediately = true,
        Rewindable = false,
    };

    /// <summary>Visit each result, disposing it afterwards; return false from <paramref name="visit"/> to stop.</summary>
    public static void ForEach(ManagementObjectSearcher searcher, Func<ManagementBaseObject, bool> visit)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentNullException.ThrowIfNull(visit);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                if (!visit(item)) return;
            }
        }
    }
}
