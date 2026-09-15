using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using ActualChat.Module;

namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxTtsClientTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

    [Fact]
    public void SplitTextShouldSplitLongTextIntoBoundedParts()
    {
        // arrange
        var text = string.Concat(Enumerable.Repeat("Sentence one. ", 800));

        // act
        var parts = SonioxTtsClient.SplitText(text, 5000).ToList();

        // assert
        parts.Count.Should().BeGreaterThanOrEqualTo(3);
        parts.Should().OnlyContain(p => p.Length <= 5000);
        parts.Should().OnlyContain(p => p.EndsWith('.') || p.EndsWith(' '));
        string.Concat(parts).Should().Be(text);
    }

    [Fact(Timeout = 15_000)]
    public async Task GenerateShouldWritePcmAsTheResponseArrives()
    {
        // arrange
        var body = new Pipe();
        var client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(body.Reader.AsStream()),
        });
        var pcm = Channel.CreateUnbounded<byte[]>();
        var ct = CancellationToken.None;

        // act
        var generateTask = client.Generate("en", "Adrian", "Hello", pcm.Writer, ct);
        await body.Writer.WriteAsync(new byte[1920], ct);
        var first = await pcm.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
        var rest = new byte[100_000];
        await body.Writer.WriteAsync(rest, ct);
        await body.Writer.CompleteAsync();
        await generateTask;
        var chunks = await pcm.Reader.ReadAllAsync(ct).ToListAsync(ct);

        // assert
        first.Should().NotBeEmpty("the first PCM bytes are written before the body is complete");
        chunks.Count.Should().BeGreaterThan(1, "a body larger than the read buffer arrives in several chunks");
        (first.Length + chunks.Sum(x => x.Length)).Should().Be(1920 + rest.Length);
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldKeepOneStreamForSteadyChunks()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, CancellationToken.None);
        foreach (var chunk in new[] { "One", " two", " three." }) {
            text.Writer.TryWrite(chunk);
            await Task.Delay(20);
        }
        text.Writer.Complete();
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(1);
        soniox.ConnectionCount.Should().Be(1);
        soniox.Sent.Select(x => x.Summary).Should().Equal(
            "config s-1", "text s-1 'One '", "text s-1 ' two '", "text s-1 ' three. '", "end s-1");
        audio.Should().HaveCount(3, "the fake speaks every chunk it's fed");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldEndAnIdleStreamAndOpenANewOneForTheNextChunk()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Short, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, CancellationToken.None);
        text.Writer.TryWrite("First. ");
        await Task.Delay(Short * 4);
        text.Writer.TryWrite("Second. ");
        text.Writer.Complete();
        await runTask;

        // assert
        client.StreamCount.Should().Be(2, "the idle flush ends the first stream before the second chunk");
        soniox.ConnectionCount.Should().Be(1, "both streams share the connection");
        soniox.Sent.Select(x => x.Summary).Should().Equal(
            "config s-1", "text s-1 'First. '", "end s-1", "config s-2", "text s-2 'Second. '", "end s-2");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldRollOverAStreamPastItsDurationCap()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long, streamRollover: Short);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, CancellationToken.None);
        text.Writer.TryWrite("Old. ");
        await Task.Delay(Short * 2);
        text.Writer.TryWrite("New. ");
        text.Writer.Complete();
        await runTask;

        // assert
        client.StreamCount.Should().Be(2, "a chunk arriving past the rollover age goes to a new stream");
        soniox.ConnectionCount.Should().Be(1);
        soniox.Sent.Select(x => x.Summary).Should().Equal(
            "config s-1", "text s-1 'Old. '", "end s-1", "config s-2", "text s-2 'New. '", "end s-2");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldResendUnspokenChunksAfterSonioxKillsTheStream()
    {
        // arrange
        var soniox = new FakeSoniox { KillAfterTextCount = 2 };
        var client = NewClient(soniox, idleFlush: Short, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, CancellationToken.None);
        text.Writer.TryWrite("Spoken. ");
        await Task.Delay(Short / 3);
        text.Writer.TryWrite("Lost. ");
        await Task.Delay(Short * 4);
        text.Writer.Complete();
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(2, "the killed stream is replaced right away");
        soniox.Sent.Select(x => x.Summary).Should().Equal(
            "config s-1", "text s-1 'Spoken. '", "text s-1 'Lost. '", "config s-2", "text s-2 'Lost. '", "end s-2");
        audio.Should().HaveCount(2, "the chunk the killed stream never spoke is spoken by the next one");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldReconnectOnceWhenTheConnectionDrops()
    {
        // arrange
        var soniox = new FakeSoniox { DropAfterTextCount = 1 };
        var client = NewClient(soniox, idleFlush: Long, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, CancellationToken.None);
        text.Writer.TryWrite("Dropped. ");
        await Task.Delay(Short * 2);
        text.Writer.TryWrite("Late. ");
        text.Writer.Complete();
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        soniox.ConnectionCount.Should().Be(2);
        client.StreamCount.Should().Be(2);
        soniox.Sent.Select(x => x.Summary).Should().Equal(
            "config s-1", "text s-1 'Dropped. '",
            "config s-2", "text s-2 'Dropped. '", "text s-2 'Late. '", "end s-2");
        audio.Should().HaveCount(2);
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldFailWhenTheConnectionDropsTwice()
    {
        // arrange
        var soniox = new FakeSoniox { DropAfterTextCount = 1, DropEveryConnection = true };
        var client = NewClient(soniox, idleFlush: Short, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, CancellationToken.None);
        text.Writer.TryWrite("Doomed. ");

        // assert
        await FluentActions.Awaiting(() => runTask).Should().ThrowAsync<WebSocketException>();
        soniox.ConnectionCount.Should().Be(2, "one reconnect, then the run fails");
        await FluentActions.Awaiting(() => pcm.Reader.ReadAllAsync().ToListAsync().AsTask())
            .Should().ThrowAsync<WebSocketException>();
    }

    // Private methods

    private SonioxTtsClient NewClient(FakeSoniox soniox, TimeSpan idleFlush, TimeSpan streamRollover)
    {
        var services = new ServiceCollection()
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddTestLogging(Out);
        return new SonioxTtsClient(services.BuildServiceProvider()) {
            IdleFlush = idleFlush,
            StreamRollover = streamRollover,
            WebSocketFactory = soniox.Connect,
        };
    }

    private SonioxTtsClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var services = new ServiceCollection()
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddTestLogging(Out);
        services.AddHttpClient(SonioxTtsClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RespondingHandler(respond));
        return new SonioxTtsClient(services.BuildServiceProvider());
    }

    // Nested types

    private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond.Invoke(request));
    }

    private sealed record SentMessage(string Summary);

    // A Soniox stand-in that speaks one audio message per text and can kill a stream (408) or drop
    // the connection after a given number of texts on the first connection.
    private sealed class FakeSoniox
    {
        private static readonly string Audio = Convert.ToBase64String(new byte[1920]);

        public int KillAfterTextCount { get; init; }
        public int DropAfterTextCount { get; init; }
        public bool DropEveryConnection { get; init; }
        public int ConnectionCount { get; private set; }
        public List<SentMessage> Sent { get; } = new();

        public Task<WebSocket> Connect(CancellationToken cancellationToken)
        {
            ConnectionCount++;
            var webSocket = new FakeWebSocket();
            _ = Serve(webSocket, ConnectionCount == 1 || DropEveryConnection);
            return Task.FromResult<WebSocket>(webSocket);
        }

        private async Task Serve(FakeWebSocket webSocket, bool mayFail)
        {
            var textCount = 0;
            await foreach (var json in webSocket.Sent.Reader.ReadAllAsync()) {
                var message = JsonDocument.Parse(json).RootElement;
                var streamId = message.GetProperty("stream_id").GetString();
                if (message.TryGetProperty("api_key", out _)) {
                    lock (Sent)
                        Sent.Add(new SentMessage($"config {streamId}"));
                    continue;
                }

                var text = message.GetProperty("text").GetString() ?? "";
                var isEnd = message.GetProperty("text_end").GetBoolean();
                lock (Sent)
                    Sent.Add(new SentMessage(isEnd ? $"end {streamId}" : $"text {streamId} '{text}'"));
                if (text.Length > 0) {
                    textCount++;
                    if (mayFail && textCount == DropAfterTextCount) {
                        webSocket.Incoming.Writer.TryComplete();
                        return;
                    }
                    if (mayFail && textCount == KillAfterTextCount) {
                        webSocket.Incoming.Writer.TryWrite(
                            $$"""{"stream_id":"{{streamId}}","error_code":408,"error_message":"Stream killed"}""");
                        continue;
                    }
                    webSocket.Incoming.Writer.TryWrite($$"""{"stream_id":"{{streamId}}","audio":"{{Audio}}"}""");
                }
                if (isEnd)
                    webSocket.Incoming.Writer.TryWrite($$"""{"stream_id":"{{streamId}}","terminated":true}""");
            }
        }
    }

    private sealed class FakeWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public Channel<string> Sent { get; } = Channel.CreateUnbounded<string>();
        public Channel<string> Incoming { get; } = Channel.CreateUnbounded<string>();

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => Close(WebSocketState.Aborted);
        public override void Dispose() => Close(WebSocketState.Closed);

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            Close(WebSocketState.Closed);
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            Close(WebSocketState.CloseSent);
            return Task.CompletedTask;
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (!await Incoming.Reader.WaitToReadAsync(cancellationToken))
                return new WebSocketReceiveResult(
                    0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "");

            var message = await Incoming.Reader.ReadAsync(cancellationToken);
            var count = Encoding.UTF8.GetBytes(message, buffer);
            return new WebSocketReceiveResult(count, WebSocketMessageType.Text, true);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (_state != WebSocketState.Open)
                throw new WebSocketException(WebSocketError.InvalidState, "The socket is not open.");

            Sent.Writer.TryWrite(Encoding.UTF8.GetString(buffer));
            return Task.CompletedTask;
        }

        private void Close(WebSocketState state)
        {
            _state = state;
            Sent.Writer.TryComplete();
            Incoming.Writer.TryComplete();
        }
    }
}
