using System.Net;
using System.Text;
using ActualLab.IO;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public async Task OverlappingRequestsShouldDownloadOnce(
        bool mustNormalizeUrl, bool mustUseAnotherHandler, bool mustVaryDirectoryPath)
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = new PausingStream();
        var downloadCount = 0;
        var options = new FileSystemContentHandler.Options {
            Directory = _directory,
            EncryptionKey = _key,
            CacheUrlNormalizer = url => mustNormalizeUrl ? new Uri(url.GetLeftPart(UriPartial.Path)) : url,
        };
        var source = new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("body"));
        var handler = new FileSystemContentHandler(options, source);
        if (mustVaryDirectoryPath) {
            Directory.CreateDirectory(_directory);
            FilePath equivalentDirectory = _directory.Value.ToUpperInvariant();
            if (!Directory.Exists(equivalentDirectory))
                equivalentDirectory = _directory & ".";
            options = options with { Directory = equivalentDirectory };
        }
        var secondHandler = mustUseAnotherHandler ? new FileSystemContentHandler(options, source) : handler;
        var firstRequest = new ContentRequest(new Uri("https://cdn.example/image?signature=first"));
        var secondRequest = mustNormalizeUrl
            ? new ContentRequest(new Uri("https://cdn.example/image?signature=renewed"))
            : firstRequest;

        // act
        using var first = await handler.Handle(firstRequest, timeout.Token);
        await stream.WhenReading.WaitAsync(timeout.Token);
        using var second = await secondHandler.Handle(secondRequest, timeout.Token);
        stream.Release();
        var firstBody = await first!.Content.ReadAsStreamAsync(timeout.Token);
        var secondBody = await second!.Content.ReadAsStreamAsync(timeout.Token);
        var firstByte = new byte[1];
        await firstBody.ReadExactlyAsync(firstByte, timeout.Token);

        // assert
        downloadCount.Should().Be(1);
        first.Should().NotBeSameAs(second);
        firstByte[0].Should().Be((byte)'b');
        secondBody.Position.Should().Be(0);
        first.Dispose();
        (await second.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        GetCacheFiles().Should().ContainSingle().Which.Should().NotEndWith(".p");
    }

    [Fact]
    public async Task CancelingAReaderShouldNotCancelOtherReaders()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        using var stream = new PausingStream();
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("body")));
        using var first = await handler.Handle(Request(), cancellation.Token);
        using var second = await handler.Handle(Request(), timeout.Token);
        var firstReadTask = first!.Content.ReadAsStringAsync(timeout.Token);
        var secondReadTask = second!.Content.ReadAsStringAsync(timeout.Token);
        await stream.WhenReading.WaitAsync(timeout.Token);

        // act
        cancellation.Cancel();
        var action = async () => await firstReadTask.WaitAsync(timeout.Token);

        // assert
        try {
            await action.Should().ThrowAsync<OperationCanceledException>();
            secondReadTask.IsCompleted.Should().BeFalse();
        }
        finally {
            stream.Release();
        }
        (await secondReadTask.WaitAsync(timeout.Token)).Should().Be("body");
        using var cached = await handler.Handle(Request(), timeout.Token);
        (await cached!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        downloadCount.Should().Be(1);
    }

    [Fact]
    public async Task FailedDownloadsShouldFailReadersAndAllowANewRequestToRetry()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = new PausingStream { ReadError = new IOException("Download failed") };
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("body")));
        using var first = await handler.Handle(Request(), timeout.Token);
        await stream.WhenReading.WaitAsync(timeout.Token);
        using var second = await handler.Handle(Request(), timeout.Token);
        var firstReadTask = first!.Content.ReadAsStringAsync(timeout.Token);
        var secondReadTask = second!.Content.ReadAsStringAsync(timeout.Token);

        // act
        stream.Release();
        var firstAction = async () => await firstReadTask.WaitAsync(timeout.Token);
        var secondAction = async () => await secondReadTask.WaitAsync(timeout.Token);

        // assert
        (await firstAction.Should().ThrowAsync<HttpRequestException>()).WithInnerException<IOException>();
        (await secondAction.Should().ThrowAsync<HttpRequestException>()).WithInnerException<IOException>();
        using var retry = await handler.Handle(Request(), timeout.Token);
        (await retry!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        downloadCount.Should().Be(2);
        GetCacheFiles().Should().ContainSingle().Which.Should().NotEndWith(".p");
    }

    [Fact]
    public async Task DifferentUrlsShouldDownloadInParallel()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = new PausingStream();
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("other")));
        using var first = await handler.Handle(Request(), timeout.Token);
        var firstReadTask = first!.Content.ReadAsStringAsync(timeout.Token);
        await stream.WhenReading.WaitAsync(timeout.Token);

        // act
        try {
            using var other = await handler.Handle(
                new ContentRequest(new Uri("https://cdn.example/other")), timeout.Token);

            // assert
            (await other!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("other");
            firstReadTask.IsCompleted.Should().BeFalse();
            downloadCount.Should().Be(2);
        }
        finally {
            stream.Release();
        }
        (await firstReadTask.WaitAsync(timeout.Token)).Should().Be("body");
    }

    [Fact]
    public async Task DisposingTheLastReaderShouldCancelTheSourceAndPermitRetry()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = new PausingStream();
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("retry")));
        var response = await handler.Handle(Request(), timeout.Token);
        await stream.WhenReading.WaitAsync(timeout.Token);

        // act
        response!.Dispose();
        using var retry = await handler.Handle(Request(), timeout.Token);

        // assert
        (await retry!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("retry");
        stream.CanRead.Should().BeFalse();
        downloadCount.Should().Be(2);
        GetCacheFiles().Should().ContainSingle().Which.Should().NotEndWith(".p");
    }

    [Fact]
    public async Task CancelingAnIdleRequestShouldReleaseItsDownload()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        using var source = new PausingStream();
        var handler = Create(new TestSource(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) }));
        using var response = await handler.Handle(Request(), cancellation.Token);
        await source.WhenReading.WaitAsync(timeout.Token);

        // act
        cancellation.Cancel();
        await source.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);

        // assert
        source.CanRead.Should().BeFalse();
    }

    // Nested types

    private sealed class PausingStream() : MemoryStream(Encoding.UTF8.GetBytes("body"))
    {
        private readonly TaskCompletionSource _whenReadingSource = TaskCompletionSourceExt.New();
        private readonly TaskCompletionSource _whenReleasedSource = TaskCompletionSourceExt.New();

        private readonly TaskCompletionSource _whenDisposedSource = TaskCompletionSourceExt.New();

        public Task WhenReading => _whenReadingSource.Task;
        public Task WhenDisposed => _whenDisposedSource.Task;
        public Exception? ReadError { get; init; }

        public void Release() => _whenReleasedSource.TrySetResult();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _whenDisposedSource.TrySetResult();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _whenReadingSource.TrySetResult();
            await _whenReleasedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (ReadError != null)
                throw ReadError;
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
}
