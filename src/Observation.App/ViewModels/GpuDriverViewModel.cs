using Observation.App.Services;
using Observation.Handlers.Diag;

namespace Observation.App.ViewModels;

/// <summary>
/// Один видеоадаптер из GpuDriverInfo — «открыть страницу драйверов» на известные страницы
/// вендора (NVIDIA/AMD/Intel, тот же уровень доверия, что у winget package ID — известные, но
/// не проверенные вживую в этой сессии URL), иначе честный веб-поиск вместо гадания на URL.
/// </summary>
public sealed class GpuDriverViewModel
{
    private static readonly (string Match, string Url)[] KnownVendorPages =
    {
        ("NVIDIA", "https://www.nvidia.com/Download/index.aspx"),
        ("Advanced Micro Devices", "https://www.amd.com/en/support"),
        ("AMD", "https://www.amd.com/en/support"),
        ("Intel", "https://www.intel.com/content/www/us/en/support/detect.html")
    };

    public GpuDriverInfo Info { get; }
    public string Name => Info.Name;
    public string VersionLabel => $"{Info.DriverVersion} · {Info.DriverDate}";

    public RelayCommand OpenDriverPageCommand { get; }

    public GpuDriverViewModel(GpuDriverInfo info)
    {
        Info = info;
        OpenDriverPageCommand = new RelayCommand(OpenDriverPage);
    }

    private void OpenDriverPage()
    {
        var known = KnownVendorPages.FirstOrDefault(v => Info.Vendor.Contains(v.Match, StringComparison.OrdinalIgnoreCase));
        if (known.Url is not null)
            ShellLauncher.Launch(known.Url);
        else
            ShellLauncher.SearchOnline($"{Info.Vendor} {Info.Name} driver download");
    }
}
