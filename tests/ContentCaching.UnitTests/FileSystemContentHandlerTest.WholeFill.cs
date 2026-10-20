using System.Net;
using System.Text;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Fact]
    public async Task RangesThatMustFillWholeShouldShareOneWholeDownload()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var source = new GatedContentStream();
        var requests = new List<ContentRequest>();
        var handler = Create(new TestSource(request => {
            requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) };
            response.Content.Headers.ContentLength = 8;
            return response;
        }));

        // act
        using var head = await handler.Handle(WholeFillRange("bytes=0-1"), timeout.Token);
        var headBody = await head!.Content.ReadAsStringAsync(timeout.Token);
        using var tail = await handler.Handle(WholeFillRange("bytes=4-7"), timeout.Token);
        var tailTask = tail!.Content.ReadAsStringAsync(timeout.Token);
        var isTailWaiting = !tailTask.IsCompleted;
        source.Release();
        var tailBody = await tailTask;

        // assert
        headBody.Should().Be("he");
        head.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        head.Content.Headers.ContentRange!.ToString().Should().Be("bytes 0-1/8");
        head.Headers.AcceptRanges.Should().Contain("bytes");
        isTailWaiting.Should().BeTrue("the fill has not reached the tail yet");
        tailBody.Should().Be("tail");
        tail.Content.Headers.ContentRange!.ToString().Should().Be("bytes 4-7/8");
        requests.Should().ContainSingle().Which.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task AWholeFillShouldOutliveItsRangeReaders()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var source = new GatedContentStream();
        var handler = Create(new TestSource(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) };
            response.Content.Headers.ContentLength = 8;
            return response;
        }));
        using (var head = await handler.Handle(WholeFillRange("bytes=0-3"), timeout.Token))
            (await head!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("head");

        // act
        source.Release();
        await WhenMet(() => GetCacheFiles() is [var file] && !file.EndsWith(".p"));
        var offline = Create(new TestSource(_ => throw new InvalidOperationException("Offline")));
        using var whole = await offline.Handle(Request(), timeout.Token);

        // assert
        (await whole!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("headtail");
    }

    [Fact]
    public async Task AnUnsatisfiableRangeShouldStillStartTheWholeFill()
    {
        // arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var handler = Create(new TestSource(_ => Response("headtail")));

        // act
        using var response = await handler.Handle(WholeFillRange("bytes=100-"), timeout.Token);
        await WhenMet(() => GetCacheFiles() is [var file] && !file.EndsWith(".p"));
        using var cached = await handler.Handle(WholeFillRange("bytes=4-"), timeout.Token);

        // assert
        response!.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes */8");
        (await cached!.Content.ReadAsStringAsync(timeout.Token)).Should().Be("tail");
    }

    [Theory]
    [InlineData("unknown length")]
    [InlineData("no-store")]
    public async Task ARangeWhoseWholeResourceBypassesShouldBeFetchedOnItsOwn(string scenario)
    {
        // arrange
        var requests = new List<ContentRequest>();
        var handler = Create(new TestSource(request => {
            requests.Add(request);
            if (request.Headers.Count != 0) {
                var partial = Response("he");
                partial.StatusCode = HttpStatusCode.PartialContent;
                partial.Content.Headers.ContentRange = new(0, 1, 8);
                return partial;
            }

            var whole = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("headtail"))),
            };
            if (scenario == "no-store")
                whole.Headers.CacheControl = new() { NoStore = true };
            else
                whole.Content.Headers.ContentLength = null;
            return whole;
        }));

        // act
        using var response = await handler.Handle(WholeFillRange("bytes=0-1"));

        // assert
        (await response!.Content.ReadAsStringAsync()).Should().Be("he");
        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        requests.Select(x => x.Headers.Count).Should().Equal(0, 1);
        GetCacheFiles().Should().BeEmpty();
    }

    // Private methods

    private static ContentRequest WholeFillRange(string range)
        => Request() with {
            Headers = new Dictionary<string, string> { ["Range"] = range },
            MustFillWhole = true,
        };
}
