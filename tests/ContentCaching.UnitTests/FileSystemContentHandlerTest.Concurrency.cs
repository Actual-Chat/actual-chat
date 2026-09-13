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
        var firstTask = handler.Handle(firstRequest, timeout.Token).AsTask();
        await stream.WhenReading.WaitAsync(timeout.Token);
        var secondTask = secondHandler.Handle(secondRequest, timeout.Token).AsTask();
        stream.Release();
        using var first = await firstTask.WaitAsync(timeout.Token);
        using var second = await secondTask.WaitAsync(timeout.Token);

        // assert
        downloadCount.Should().Be(1);
        first.Should().NotBeSameAs(second);
        var firstBody = await first!.Content.ReadAsStreamAsync(timeout.Token);
        var secondBody = await second!.Content.ReadAsStreamAsync(timeout.Token);
        firstBody.ReadByte().Should().Be((byte)'b');
        secondBody.Position.Should().Be(0);
        first.Dispose();
        (await second.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        GetCacheFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task CancelingAWaiterShouldNotCancelTheDownload()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        using var stream = new PausingStream();
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("body")));
        var firstTask = handler.Handle(Request(), timeout.Token).AsTask();
        await stream.WhenReading.WaitAsync(timeout.Token);

        // act
        var waitingTask = handler.Handle(Request(), cancellation.Token).AsTask();
        cancellation.Cancel();
        var action = async () => { using var response = await waitingTask.WaitAsync(timeout.Token); };

        // assert
        try {
            await action.Should().ThrowAsync<OperationCanceledException>();
            firstTask.IsCompleted.Should().BeFalse();
        }
        finally {
            stream.Release();
        }
        using var first = await firstTask.WaitAsync(timeout.Token);
        using var cached = await handler.Handle(Request(), timeout.Token);
        (await first!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        (await cached!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        downloadCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDownloadShouldAllowAWaiterToRetry(bool mustCancel)
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var stream = new PausingStream { ReadError = mustCancel ? null : new IOException("Download failed") };
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => Interlocked.Increment(ref downloadCount) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            : Response("body")));
        var firstTask = handler.Handle(Request(), cancellation.Token).AsTask();
        await stream.WhenReading.WaitAsync(timeout.Token);

        // act
        var waitingTask = handler.Handle(Request(), timeout.Token).AsTask();
        if (mustCancel)
            cancellation.Cancel();
        else
            stream.Release();
        var action = async () => { using var response = await firstTask.WaitAsync(timeout.Token); };

        // assert
        if (mustCancel)
            await action.Should().ThrowAsync<OperationCanceledException>();
        else
            await action.Should().ThrowAsync<IOException>();
        using var waiting = await waitingTask.WaitAsync(timeout.Token);
        using var cached = await handler.Handle(Request(), timeout.Token);
        (await waiting!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        (await cached!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
        downloadCount.Should().Be(2);
        GetCacheFiles().Should().ContainSingle();
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
        var firstTask = handler.Handle(Request(), timeout.Token).AsTask();
        await stream.WhenReading.WaitAsync(timeout.Token);

        // act
        try {
            using var other = await handler.Handle(
                new ContentRequest(new Uri("https://cdn.example/other")), timeout.Token);

            // assert
            (await other!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("other");
            firstTask.IsCompleted.Should().BeFalse();
            downloadCount.Should().Be(2);
        }
        finally {
            stream.Release();
        }
        using var first = await firstTask.WaitAsync(timeout.Token);
        (await first!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("body");
    }

    // Nested types

    private sealed class PausingStream() : MemoryStream(Encoding.UTF8.GetBytes("body"))
    {
        private readonly TaskCompletionSource _whenReadingSource = TaskCompletionSourceExt.New();
        private readonly TaskCompletionSource _whenReleasedSource = TaskCompletionSourceExt.New();

        public Task WhenReading => _whenReadingSource.Task;
        public Exception? ReadError { get; init; }

        public void Release() => _whenReleasedSource.TrySetResult();

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
