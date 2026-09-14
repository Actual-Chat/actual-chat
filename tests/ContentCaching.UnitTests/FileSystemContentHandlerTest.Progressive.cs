using System.Net;
using System.Text;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadersShouldReceiveBytesWhileTheSharedDownloadIsStillRunning(bool isRange)
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var sourceStream = new GatedContentStream();
        var request = isRange
            ? Request() with { Headers = new Dictionary<string, string> { ["Range"] = "bytes=1000000-1000007" } }
            : Request();
        var downloads = 0;
        var handler = Create(new TestSource(_ => {
            Interlocked.Increment(ref downloads);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(sourceStream) };
            response.Content.Headers.ContentLength = 8;
            if (isRange) {
                response.StatusCode = HttpStatusCode.PartialContent;
                response.Content.Headers.ContentRange = new(1000000, 1000007, 1000008);
            }
            return response;
        }));
        var firstTask = handler.Handle(request, timeout.Token).AsTask();

        try {
            // act
            using var first = await firstTask.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);
            var firstStream = await first!.Content.ReadAsStreamAsync(timeout.Token);
            var firstBytes = new byte[4];
            await firstStream.ReadExactlyAsync(firstBytes, timeout.Token);
            using var second = await handler.Handle(request, timeout.Token);
            var secondStream = await second!.Content.ReadAsStreamAsync(timeout.Token);
            var secondBytes = new byte[4];
            await secondStream.ReadExactlyAsync(secondBytes, timeout.Token);

            // assert
            Encoding.UTF8.GetString(firstBytes).Should().Be("head");
            secondBytes.Should().Equal(firstBytes);
            downloads.Should().Be(1);
            GetCacheFiles().Should().ContainSingle().Which.Should().EndWith(".p");
            sourceStream.Release();
            (await first.Content.ReadAsStringAsync(timeout.Token)).Should().Be("tail");
            (await second.Content.ReadAsStringAsync(timeout.Token)).Should().Be("tail");
            GetCacheFiles().Should().ContainSingle().Which.Should().NotEndWith(".p");
        }
        finally {
            sourceStream.Release();
            using var response = await firstTask.WaitAsync(timeout.Token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeAndUnknownLengthBodiesShouldBeCached(bool isLengthUnknown)
    {
        // arrange
        var body = new string('v', 2 * 1024 * 1024 + 17);
        using var sourceStream = new ContentHandlerTest.CountingStream(body) { IsLengthHidden = isLengthUnknown };
        var source = new TestSource(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(sourceStream) };
            if (!isLengthUnknown)
                response.Content.Headers.ContentLength = body.Length;
            return response;
        });
        var handler = Create(source);

        // act
        using (var response = await handler.Handle(Request()))
            (await response!.Content.ReadAsStringAsync()).Should().Be(body);
        using var cached = await Create(new TestSource(_ => throw new InvalidOperationException("Offline")))
            .Handle(Request());

        // assert
        (await cached!.Content.ReadAsStringAsync()).Should().Be(body);
        cached.Content.Headers.ContentLength.Should().Be(body.Length);
        GetCacheFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task AColdVideoRangeShouldBeCachedWithoutDownloadingThePrefix()
    {
        // arrange
        var request = Request() with {
            Headers = new Dictionary<string, string> { ["Range"] = "bytes=1000000-1000007" },
        };
        var source = new TestSource(actual => {
            actual.Headers["Range"].Should().Be("bytes=1000000-1000007");
            var response = Response("taildata");
            response.StatusCode = HttpStatusCode.PartialContent;
            response.Content.Headers.ContentRange = new(1000000, 1000007, 1000008);
            return response;
        });

        // act
        using (var first = await Create(source).Handle(request))
            (await first!.Content.ReadAsStringAsync()).Should().Be("taildata");
        using var cached = await Create(new TestSource(_ => throw new InvalidOperationException("Offline")))
            .Handle(request);

        // assert
        cached!.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        cached.Content.Headers.ContentRange!.From.Should().Be(1000000);
        (await cached.Content.ReadAsStringAsync()).Should().Be("taildata");
    }

    [Theory]
    [InlineData("bytes=2-5", "2345", 2, 5)]
    [InlineData("bytes=7-", "789", 7, 9)]
    [InlineData("bytes=-3", "789", 7, 9)]
    public async Task CompletedVideosShouldServeByteRanges(
        string range, string expected, long start, long end)
    {
        // arrange
        using (var first = await Create(new TestSource(_ => Response("0123456789"))).Handle(Request()))
            (await first!.Content.ReadAsStringAsync()).Should().Be("0123456789");
        var request = Request() with { Headers = new Dictionary<string, string> { ["Range"] = range } };

        // act
        using var cached = await Create(new TestSource(_ => throw new InvalidOperationException("Offline")))
            .Handle(request);

        // assert
        cached!.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        cached.Content.Headers.ContentRange!.From.Should().Be(start);
        cached.Content.Headers.ContentRange.To.Should().Be(end);
        cached.Content.Headers.ContentRange.Length.Should().Be(10);
        (await cached.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    // Nested types

    private sealed class GatedContentStream() : MemoryStream(Encoding.UTF8.GetBytes("headtail"))
    {
        private readonly TaskCompletionSource _whenReleasedSource = TaskCompletionSourceExt.New();

        public void Release() => _whenReleasedSource.TrySetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= 4)
                await _whenReleasedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            else
                buffer = buffer[..Math.Min(buffer.Length, (int)(4 - Position))];
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
}
