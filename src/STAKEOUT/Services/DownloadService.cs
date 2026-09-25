using System.IO;
using System.Net.Http;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>
/// Downloads a file to the user's Downloads folder, reporting 0..100 % progress
/// so the UI progress bar can animate. Streams to disk (no full buffering) and
/// supports cancellation.
/// </summary>
public sealed class DownloadService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    /// <summary>Absolute path of the current user's Downloads folder.</summary>
    public static string DownloadsFolder
    {
        get
        {
            // SpecialFolder has no Downloads entry; derive it from the profile.
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(profile, "Downloads");
            try { Directory.CreateDirectory(downloads); } catch { /* fall back below */ }
            return Directory.Exists(downloads) ? downloads : Path.GetTempPath();
        }
    }

    /// <summary>
    /// Download <paramref name="url"/> into Downloads as <paramref name="fileName"/>.
    /// Returns the saved path, or null on failure.
    /// </summary>
    public async Task<string?> DownloadAsync(string url, string fileName,
        IProgress<double> progress, CancellationToken ct = default)
    {
        var dest = Path.Combine(DownloadsFolder, fileName);
        try
        {
            Logger.Log("Download", "START", fileName);
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(dest, FileMode.Create, FileAccess.Write,
                FileShare.None, 1 << 16, useAsync: true);

            var buffer = new byte[1 << 16];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                readTotal += read;
                if (total > 0)
                    progress.Report(Math.Min(100d, readTotal * 100d / total));
            }
            progress.Report(100d);
            Logger.Log("Download", "DONE", $"{fileName} ({readTotal / 1024 / 1024} MB)");
            return dest;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Download", "CANCELLED", fileName);
            TryDelete(dest);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError("Download " + fileName, ex);
            TryDelete(dest);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
