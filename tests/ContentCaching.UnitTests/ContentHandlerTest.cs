using System.Net;
using System.Text;

namespace ActualChat.ContentCaching.UnitTests;

public sealed class ContentHandlerTest
{
    [Fact]
    public async Task PassThroughShouldLogWithoutLeakingTheUrl()
    {
        // arrange
        var logger = new ListLogger();
        var handler = new LoggingContentHandler(log: logger);
        var request = new ContentRequest(new Uri("https://user:password@cdn.example/private-file?token=secret"));

        // act
        using var response = await handler.Handle(request);

        // assert
        response.Should().BeNull();
        logger.Messages.Should().NotBeEmpty();
        string.Join("\n", logger.Messages).Should().Contain("cdn.example")
            .And.NotContain("password").And.NotContain("secret").And.NotContain("private-file");
    }

    [Fact]
    public async Task HttpHandlerShouldPreserveRangeAndReturnBeforeReadingContent()
    {
        // arrange
        using var body = new CountingStream("partial body");
        using var client = new HttpClient(new TestHttpHandler(request => {
            request.RequestUri!.AbsoluteUri.Should().Be("https://cdn.example/video?variant=original");
            request.Headers.Range!.ToString().Should().Be("bytes=10-21");
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(body) };
            response.Content.Headers.ContentRange = new(10, 21, 100);
            return response;
        }));
        var handler = new HttpContentHandler(client);
        var request = new ContentRequest(new Uri("https://cdn.example/video?variant=original")) {
            Headers = new Dictionary<string, string> { ["Range"] = "bytes=10-21" },
        };

        // act
        using var response = await handler.Handle(request);

        // assert
        response.Should().NotBeNull();
        body.ReadCount.Should().Be(0, "headers should be returned without buffering media");
        response!.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes 10-21/100");
        (await response.Content.ReadAsStringAsync()).Should().Be("partial body");
        body.ReadCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LoggingDecoratorShouldPreserveDownstreamResponse()
    {
        // arrange
        using var client = new HttpClient(new TestHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var handler = new LoggingContentHandler(new HttpContentHandler(client), new ListLogger());

        // act
        using var response = await handler.Handle(new ContentRequest(new Uri("https://cdn.example/missing")));

        // assert
        response!.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Nested types

    internal sealed class CountingStream(string text) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        public int ReadCount { get; private set; }
        public bool IsLengthHidden { get; set; }
        public override bool CanSeek => !IsLengthHidden;

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ReadCount++;
            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }
    }

    private sealed class TestHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
