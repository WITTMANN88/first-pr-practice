using System.Security.Authentication;
using System.Security.Cryptography;
using Stakeout.Core;

namespace Stakeout.Services;

/// <summary>How a download ended.</summary>
public enum DownloadStatus
{
    /// <summary>Saved, and its SHA-256 matched the reference.</summary>
    Completed,
    Failed,
    Cancelled,
    /// <summary>Refused before any request: not HTTPS, or not a plain file name.</summary>
    Blocked,
    /// <summary>Refused before any request: no usable reference SHA-256 (e.g. the placeholder).</summary>
    HashNotConfigured,
    /// <summary>Downloaded, but the SHA-256 differs from the reference; the file was deleted.</summary>
    HashMismatch,
}

/// <summary>Outcome of <see cref="DownloadService.DownloadAsync"/>.</summary>
/// <param name="Status">How the download ended.</param>
/// <param name="Path">Saved file (only when <see cref="DownloadStatus.Completed"/>).</param>
/// <param name="ActualSha256">SHA-256 of the received bytes, when the download got that far.</param>
public sealed record DownloadResult(DownloadStatus Status, string? Path = null, string? ActualSha256 = null);

/// <summary>
/// Downloads an installer into the user's Downloads folder with 0..100 %
/// progress, and verifies it against a pinned SHA-256 before it may be run.
///
/// Integrity: the hash is computed while streaming (no second pass). Bytes go
/// to "&lt;name&gt;.partial" and only a verified file is renamed to its final
/// name, so a tampered or truncated installer never sits under the name the
/// user would double-click; on mismatch it is deleted. Transport: HTTPS only,
/// with certificate revocation checked (an unreachable revocation server fails
/// the download: this client fetches programs that are run elevated).
/// </summary>
public sealed class DownloadService : IDisposable
{
    private const int BufferSize = 1 << 16;

    // One client for the service's lifetime (a DI singleton), so connections are
    // pooled; disposed with the container at exit.
    private readonly HttpClient _http;
    private readonly Func<string> _downloadsFolder;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(30);

    public DownloadService()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            CheckCertificateRevocationList = true,
        })
        {
            Timeout = RequestTimeout,
        };
        _downloadsFolder = () => DownloadsFolder;
    }

    /// <summary>Test hook: a fake transport and a scratch destination folder.</summary>
    internal DownloadService(HttpMessageHandler handler, Func<string> downloadsFolder)
    {
        _http = new HttpClient(handler) { Timeout = RequestTimeout };
        _downloadsFolder = downloadsFolder;
    }

    /// <summary>Absolute path of the current user's Downloads folder.</summary>
    public static string DownloadsFolder
    {
        get
        {
            // SpecialFolder has no Downloads entry; derive it from the profile.
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(profile, "Downloads");
            try
            {
                Directory.CreateDirectory(downloads);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // fall back below
            }
            return Directory.Exists(downloads) ? downloads : Path.GetTempPath();
        }
    }

    /// <summary>
    /// Download <paramref name="url"/> into Downloads as <paramref name="fileName"/>
    /// and verify it against <paramref name="expectedSha256"/>.
    /// </summary>
    public async Task<DownloadResult> DownloadAsync(Uri url, string fileName, string? expectedSha256,
        IProgress<double> progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(progress);

        // Installers are executed afterwards: refuse anything but HTTPS, and any
        // file name that could point outside the Downloads folder.
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            Logger.Log("Download", "BLOCKED", $"{fileName}: non-HTTPS URL refused");
            return new DownloadResult(DownloadStatus.Blocked);
        }
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            Logger.Log("Download", "BLOCKED", $"'{fileName}' is not a plain file name");
            return new DownloadResult(DownloadStatus.Blocked);
        }
        if (!Sha256Hash.TryParse(expectedSha256, out var expected))
        {
            Logger.Log("Download", "BLOCKED", $"{fileName}: no reference SHA-256 configured; download refused");
            return new DownloadResult(DownloadStatus.HashNotConfigured);
        }

        var dest = Path.Combine(_downloadsFolder(), fileName);
        var partial = dest + ".partial";
        try
        {
            Logger.Log("Download", "START", fileName);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            byte[] actual;
            long received = 0;
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (input.ConfigureAwait(false))
                {
                    var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                    await using (output.ConfigureAwait(false))
                    {
                        var buffer = new byte[BufferSize];
                        int read;
                        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            sha.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            received += read;
                            if (total > 0) progress.Report(Math.Min(100d, received * 100d / total));
                        }
                    }
                }
                actual = sha.GetHashAndReset();
            }

            var actualHex = Sha256Hash.ToHex(actual);
            if (!Sha256Hash.Matches(actual, expected))
            {
                TryDelete(partial);
                Logger.Log("Download", "CRITICAL",
                    $"{fileName}: SHA-256 mismatch (expected {Sha256Hash.ToHex(expected)}, got {actualHex}); file deleted, install aborted");
                return new DownloadResult(DownloadStatus.HashMismatch, ActualSha256: actualHex);
            }

            File.Move(partial, dest, overwrite: true);
            progress.Report(100d);
            Logger.Log("Download", "DONE", $"{fileName} ({received / 1024 / 1024} MB, SHA-256 verified)");
            return new DownloadResult(DownloadStatus.Completed, dest, actualHex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.Log("Download", "CANCELLED", fileName);
            TryDelete(partial);
            return new DownloadResult(DownloadStatus.Cancelled);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            // Includes an unreachable revocation server: fail closed, by design.
            Logger.Log("Download", "TLS_REJECTED", $"{fileName}: {ex.InnerException.Message}");
            TryDelete(partial);
            return new DownloadResult(DownloadStatus.Failed);
        }
        catch (Exception ex)
        {
            Logger.LogError("Download " + fileName, ex);
            TryDelete(partial);
            return new DownloadResult(DownloadStatus.Failed);
        }
    }

    public void Dispose() => _http.Dispose();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogError("Download.Cleanup", ex);
        }
    }
}
