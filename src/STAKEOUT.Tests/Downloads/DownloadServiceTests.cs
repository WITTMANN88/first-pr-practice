using System.Net;
using System.Security.Cryptography;
using Stakeout.Core;
using Stakeout.Services;
using Stakeout.Tests.TestSupport;

namespace Stakeout.Tests.Downloads;

/// <summary>Download + SHA-256 verification against a fake transport (no network).</summary>
public sealed class DownloadServiceTests : IDisposable
{
    private static readonly Uri Url = new("https://vendor.example/setup.exe");
    private static readonly byte[] Installer = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31)).ToArray();
    private static readonly string InstallerSha = Convert.ToHexString(SHA256.HashData(Installer));

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Counts requests; owned by the test, while the handler is owned by the service.</summary>
    private sealed class RequestLog
    {
        public int Count { get; set; }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpContent> _content;
        private readonly HttpStatusCode _status;
        private readonly RequestLog _log;

        public StubHandler(Func<HttpContent> content, HttpStatusCode status, RequestLog log)
        {
            _content = content;
            _status = status;
            _log = log;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _log.Count++;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = _content() });
        }
    }

    private sealed class Recorder : IProgress<double>
    {
        public List<double> Values { get; } = new();
        public void Report(double value) => Values.Add(value);
    }

    private readonly RequestLog _requests = new();

    private DownloadService Create(Func<HttpContent>? content = null, HttpStatusCode status = HttpStatusCode.OK)
        => new(new StubHandler(content ?? (() => new ByteArrayContent(Installer)), status, _requests), () => _dir.Path);

    private string[] FilesInFolder() => Directory.GetFiles(_dir.Path).Select(Path.GetFileName).OrderBy(f => f).ToArray()!;

    [Fact]
    public async Task MatchingHash_SavesTheFile_UnderItsFinalName()
    {
        using var service = Create();
        var progress = new Recorder();

        var result = await service.DownloadAsync(Url, "setup.exe", InstallerSha, progress);

        Assert.Equal(DownloadStatus.Completed, result.Status);
        Assert.Equal(Path.Combine(_dir.Path, "setup.exe"), result.Path);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(result.Path!));
        Assert.Equal(InstallerSha, result.ActualSha256);
        Assert.Equal(new[] { "setup.exe" }, FilesInFolder());          // no .partial left
        Assert.Equal(100d, progress.Values[^1]);
        Assert.Equal(progress.Values.OrderBy(v => v), progress.Values);  // monotonic
    }

    [Fact]
    public async Task MismatchingHash_DeletesTheFile_AndReportsWhatWasReceived()
    {
        using var service = Create();
        var wrong = new string('0', 64);

        var result = await service.DownloadAsync(Url, "setup.exe", wrong, new Recorder());

        Assert.Equal(DownloadStatus.HashMismatch, result.Status);
        Assert.Null(result.Path);
        Assert.Equal(InstallerSha, result.ActualSha256);
        Assert.Empty(FilesInFolder());                                  // nothing runnable left behind
    }

    [Fact]
    public async Task TamperedByte_IsCaught()
    {
        var tampered = (byte[])Installer.Clone();
        tampered[123_456] ^= 1;
        using var service = Create(() => new ByteArrayContent(tampered));

        var result = await service.DownloadAsync(Url, "setup.exe", InstallerSha, new Recorder());

        Assert.Equal(DownloadStatus.HashMismatch, result.Status);
        Assert.Empty(FilesInFolder());
    }

    [Theory]
    [InlineData(Sha256Hash.Placeholder)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public async Task NoUsableReferenceHash_RefusesBeforeAnyRequest(string? expected)
    {
        using var service = Create();

        var result = await service.DownloadAsync(Url, "setup.exe", expected, new Recorder());

        Assert.Equal(DownloadStatus.HashNotConfigured, result.Status);
        Assert.Equal(0, _requests.Count);
        Assert.Empty(FilesInFolder());
    }

    [Theory]
    [InlineData("http://vendor.example/setup.exe", "setup.exe")]
    [InlineData("https://vendor.example/setup.exe", "../setup.exe")]
    [InlineData("https://vendor.example/setup.exe", "sub/setup.exe")]
    public async Task InsecureUrlOrPathLikeName_IsBlocked(string link, string fileName)
    {
        using var service = Create();

        var result = await service.DownloadAsync(new Uri(link), fileName, InstallerSha, new Recorder());

        Assert.Equal(DownloadStatus.Blocked, result.Status);
        Assert.Equal(0, _requests.Count);
    }

    [Fact]
    public async Task HttpError_IsAFailure_WithNothingLeftBehind()
    {
        using var service = Create(status: HttpStatusCode.NotFound);

        var result = await service.DownloadAsync(Url, "setup.exe", InstallerSha, new Recorder());

        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Empty(FilesInFolder());
    }

    /// <summary>Returns one chunk, then blocks until cancelled.</summary>
    private sealed class StallingStream : Stream
    {
        private bool _sentFirst;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sentFirst)
            {
                _sentFirst = true;
                buffer.Span[..1000].Fill(7);
                return 1000;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task CancelledMidway_RemovesThePartialFile()
    {
        using var service = Create(() => new StreamContent(new StallingStream()));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await service.DownloadAsync(Url, "setup.exe", InstallerSha, new Recorder(), cts.Token);

        Assert.Equal(DownloadStatus.Cancelled, result.Status);
        Assert.Empty(FilesInFolder());
    }
}
