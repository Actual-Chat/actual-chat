using System.Net;
using System.Security.Cryptography;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Fact]
    public async Task CorruptActiveFilesShouldNotBePublishedAfterAnotherReaderFails()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var source = new GatedContentStream();
        var handler = Create(new TestSource(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) };
            response.Content.Headers.ContentLength = 8;
            return response;
        }));
        using var first = await handler.Handle(Request(), timeout.Token);
        var firstStream = await first!.Content.ReadAsStreamAsync(timeout.Token);
        await firstStream.ReadExactlyAsync(new byte[4], timeout.Token);
        using var second = await handler.Handle(Request(), timeout.Token);
        var secondStream = await second!.Content.ReadAsStreamAsync(timeout.Token);
        var partial = GetCacheFiles().Single();
        await using (var file = new FileStream(
            partial, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) {
            file.Seek(-1, SeekOrigin.End);
            var last = file.ReadByte();
            file.Seek(-1, SeekOrigin.End);
            file.WriteByte((byte)(last ^ 1));
            await file.FlushAsync(timeout.Token);
        }

        // act
        var action = async () => await secondStream.ReadAsync(new byte[4], timeout.Token);
        try {
            await action.Should().ThrowAsync<CryptographicException>();
        }
        finally {
            source.Release();
        }
        try {
            await firstStream.CopyToAsync(Stream.Null, timeout.Token);
        }
        catch (CryptographicException) { }

        // assert
        GetCacheFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task FailedPublicationShouldResumeFromTheReadersPositionUsingTheSource()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var source = new GatedContentStream();
        var downloads = 0;
        var handler = Create(new TestSource(_ => {
            var response = Response("headtail");
            if (Interlocked.Increment(ref downloads) == 1) {
                response.Content = new StreamContent(source);
                response.Content.Headers.ContentLength = 8;
                response.Content.Headers.ContentType = new("image/png");
            }
            return response;
        }));
        using var result = await handler.Handle(Request(), timeout.Token);
        var body = await result!.Content.ReadAsStreamAsync(timeout.Token);
        await body.ReadExactlyAsync(new byte[4], timeout.Token);
        var finalPath = GetCacheFiles().Single()[..^2];
        Directory.CreateDirectory(finalPath);

        // act
        source.Release();
        using var remaining = new MemoryStream();
        await body.CopyToAsync(remaining, timeout.Token);

        // assert
        System.Text.Encoding.UTF8.GetString(remaining.ToArray()).Should().Be("tail");
        downloads.Should().Be(2);
        GetCacheFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task DisposingAResponseDuringFallbackSetupShouldDisposeTheFallback()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var source = new GatedContentStream();
        HttpResponseMessage? result = null;
        using var fallback = new CallbackStream(() => result!.Dispose());
        var downloads = 0;
        var handler = Create(new TestSource(_ => {
            var response = Response("headtail");
            response.Content = new StreamContent(Interlocked.Increment(ref downloads) == 1 ? source : fallback);
            response.Content.Headers.ContentLength = 8;
            response.Content.Headers.ContentType = new("image/png");
            return response;
        }));
        using var response = result = await handler.Handle(Request(), timeout.Token);
        var body = await response!.Content.ReadAsStreamAsync(timeout.Token);
        await body.ReadExactlyAsync(new byte[4], timeout.Token);
        Directory.CreateDirectory(GetCacheFiles().Single()[..^2]);

        // act
        source.Release();
        var action = async () => await body.CopyToAsync(Stream.Null, timeout.Token);

        // assert
        await action.Should().ThrowAsync<ObjectDisposedException>();
        fallback.CanRead.Should().BeFalse();
        downloads.Should().Be(2);
    }
    [Fact]
    public async Task OriginVaryingResponsesShouldBeCachedForRequestsWithoutOrigin()
    {
        // arrange
        var handler = Create(new TestSource(_ => {
            var response = Response("image");
            response.Headers.Vary.Add("Origin");
            return response;
        }));
        using (var first = await handler.Handle(Request()))
            (await first!.Content.ReadAsStringAsync()).Should().Be("image");

        // act
        using var cached = await Create(new TestSource(_ => throw new InvalidOperationException("Offline")))
            .Handle(Request());

        // assert
        (await cached!.Content.ReadAsStringAsync()).Should().Be("image");
        cached.Headers.Vary.Should().ContainSingle().Which.Should().Be("Origin");
    }

    [Theory]
    [InlineData("bytes=100-")]
    [InlineData("bytes=-0")]
    public async Task UnsatisfiableCachedRangesShouldReturn416(string range)
    {
        // arrange
        using (var first = await Create(new TestSource(_ => Response("body"))).Handle(Request()))
            await first!.Content.ReadAsByteArrayAsync();
        var request = Request() with { Headers = new Dictionary<string, string> { ["Range"] = range } };

        // act
        using var cached = await Create(new TestSource(_ => throw new InvalidOperationException("Offline")))
            .Handle(request);

        // assert
        cached!.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
        cached.Content.Headers.ContentRange!.Length.Should().Be(4);
        (await cached.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("bytes=0-1,4-5", null)]
    [InlineData("bytes=0-1", "If-Range")]
    [InlineData("bytes=0-1", "Cookie")]
    public async Task UnsupportedRangeRequestsShouldKeepTheDownstreamResponse(string range, string? otherHeader)
    {
        // arrange
        var upstream = Response("uncached");
        var headers = new Dictionary<string, string> { ["Range"] = range };
        if (otherHeader != null)
            headers[otherHeader] = "value";
        var request = Request() with { Headers = headers };
        var handler = Create(new TestSource(_ => upstream));

        // act
        using var response = await handler.Handle(request);

        // assert
        response.Should().BeSameAs(upstream);
        GetCacheFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task CachedSegmentsShouldNotReplaceTheWholeRepresentationOrOtherRanges()
    {
        // arrange
        var requests = new List<string>();
        var handler = Create(new TestSource(request => {
            var range = request.Headers.GetValueOrDefault("Range") ?? "";
            requests.Add(range);
            var response = Response(range == "bytes=0-3" ? "0123" : range == "bytes=4-7" ? "4567" : "01234567");
            if (range.Length != 0) {
                response.StatusCode = HttpStatusCode.PartialContent;
                response.Content.Headers.ContentRange = range == "bytes=0-3" ? new(0, 3, 8) : new(4, 7, 8);
            }
            return response;
        }));
        var firstRange = Request() with { Headers = new Dictionary<string, string> { ["Range"] = "bytes=0-3" } };
        var secondRange = Request() with { Headers = new Dictionary<string, string> { ["Range"] = "bytes=4-7" } };

        // act
        using (var first = await handler.Handle(firstRange))
            (await first!.Content.ReadAsStringAsync()).Should().Be("0123");
        using (var second = await handler.Handle(secondRange))
            (await second!.Content.ReadAsStringAsync()).Should().Be("4567");
        using var full = await handler.Handle(Request());

        // assert
        full!.StatusCode.Should().Be(HttpStatusCode.OK);
        (await full.Content.ReadAsStringAsync()).Should().Be("01234567");
        requests.Should().Equal("bytes=0-3", "bytes=4-7", "");
    }

    private sealed class CallbackStream(Action callback) : MemoryStream("headtail"u8.ToArray())
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            callback();
            return count;
        }
    }
}
