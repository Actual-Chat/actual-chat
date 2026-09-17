using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using ActualChat.Audio;
using ActualChat.Audio.Ogg;
using ActualChat.Module;
using ActualChat.Testing.Audio;

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
    public async Task GenerateShouldWriteFramesAsTheResponseArrives()
    {
        // arrange
        var body = new Pipe();
        var requestBody = "";
        var client = NewClient(r => {
            requestBody = r.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StreamContent(body.Reader.AsStream()),
            };
        });
        var output = Channel.CreateUnbounded<AudioFrame>();
        var ct = CancellationToken.None;
        var state = new OggOpusWriter.State { SerialNumber = 1 };

        // act
        var generateTask = client.Generate("en", "Adrian", "Hello", output.Writer, ct);
        await body.Writer.WriteAsync(OggOpusTestStream.WriteHeaders(state), ct);
        await body.Writer.WriteAsync(OggOpusTestStream.WritePage(state, OggOpusTestStream.Frames(1), true), ct);
        var first = await output.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
        var rest = Enumerable.Range(0, 10)
            .SelectMany(i => OggOpusTestStream.WritePage(state, OggOpusTestStream.Frames(50, 1 + 50 * i), i < 9))
            .ToArray();
        await body.Writer.WriteAsync(rest, ct);
        await body.Writer.CompleteAsync();
        await generateTask;
        var frames = await output.Reader.ReadAllAsync(ct).ToListAsync(ct);

        // assert
        requestBody.Should().Contain("\"audio_format\":\"opus\"");
        requestBody.Should().Contain("\"bitrate\":32000", "Opus is requested at the bitrate every other stream uses");
        first.Data.ToArray().Should().Equal(OggOpusTestStream.Packet(0),
            "the first frame is written before the body is complete");
        first.Offset.Should().Be(TimeSpan.Zero);
        rest.Length.Should().BeGreaterThan(32 * 1024, "a body larger than the read buffer arrives in several chunks");
        frames.Should().HaveCount(500);
        frames[^1].Offset.Should().Be(Constants.Audio.OpusFrameDuration * 500);
        frames[^1].Data.ToArray().Should().Equal(OggOpusTestStream.Packet(500));
    }

    [Fact(Timeout = 15_000)]
    public async Task GenerateShouldFailOnATruncatedResponse()
    {
        // arrange
        var body = new Pipe();
        var client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(body.Reader.AsStream()),
        });
        var output = Channel.CreateUnbounded<AudioFrame>();
        var ct = CancellationToken.None;
        var bytes = OggOpusTestStream.Write(10);

        // act
        var generateTask = client.Generate("en", "Adrian", "Hello", output.Writer, ct);
        await body.Writer.WriteAsync(bytes[..^100], ct);
        await body.Writer.CompleteAsync();

        // assert
        await FluentActions.Awaiting(() => generateTask).Should().ThrowAsync<Exception>()
            .WithMessage("*middle of an Ogg page*");
        await FluentActions.Awaiting(() => output.Reader.ReadAllAsync(ct).ToListAsync(ct).AsTask())
            .Should().ThrowAsync<Exception>("the output carries the failure");
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
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
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
        audio.Should().HaveCount(3, "the fake speaks one frame of PCM per chunk it's fed");
        audio.Should().OnlyContain(chunk => chunk.Length == OpusFramePump.FrameByteLength);
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldEndAnIdleStreamAndOpenANewOneForTheNextChunk()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Short, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act - the pre-opened stream idles out empty before the first chunk arrives
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
        await Task.Delay(Short * 2);
        text.Writer.TryWrite("First. ");
        await Task.Delay(Short * 4);
        text.Writer.TryWrite("Second. ");
        text.Writer.Complete();
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(3,
            "the empty pre-opened stream idles out, then the idle flush ends the stream carrying the first chunk");
        soniox.ConnectionCount.Should().Be(1, "all streams share the connection");
        soniox.Sent.Select(x => x.Summary).Should().Equal(
            "config s-1", "end s-1",
            "config s-2", "text s-2 'First. '", "end s-2",
            "config s-3", "text s-3 'Second. '", "end s-3");
        audio.Should().HaveCount(2, "both chunks are still spoken despite the empty first stream idling out");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldOpenTheStreamBeforeAnyText()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        var listener = new RecordingListener();

        // act
        var runTask = client.Run("s1", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
        await soniox.WhenStreamsOpened(1).WaitAsync(TimeSpan.FromSeconds(2));
        var openedBeforeText = listener.StreamsOpened;
        text.Writer.TryWrite("Hello world. ");
        text.Writer.Complete();
        await runTask;

        // assert
        soniox.OpenedStreamCount.Should().Be(1, "the stream was opened at start and reused for the first chunk");
        openedBeforeText.Should().Be(0, "the listener still sees the stream as opened on its first text");
        listener.StreamsOpened.Should().Be(1);
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
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
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
        var listener = new RecordingListener();
        var soniox = new FakeSoniox { KillAfterTextCount = 2 };
        var client = NewClient(soniox, idleFlush: Short, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
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
        listener.StreamsOpened.Should().Be(2, "the replacement stream opens too");
        listener.AudioStarts.Should().Be(2, "'Spoken.' answers before the kill, 'Lost.' after the resend");
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
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
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
    public async Task ListenerShouldSeeEachStreamOnceItHasTextAndAudio()
    {
        // arrange - two chunks in one stream, the fake answers audio for each
        var listener = new RecordingListener();
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        text.Writer.TryWrite("Hello,");
        text.Writer.TryWrite("world.");
        text.Writer.TryComplete();

        // act
        await client.Run("s1", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);

        // assert
        client.StreamCount.Should().Be(1);
        listener.StreamsOpened.Should().Be(1, "opened once, when the first chunk was sent");
        listener.AudioStarts.Should().Be(1, "reported once per stream, on its first frame's worth of PCM");
    }

    [Fact(Timeout = 15_000)]
    public async Task ListenerShouldSeeAudioStartOnceAFrameIsComplete()
    {
        // arrange - the fake answers half a frame of PCM per text
        var listener = new RecordingListener();
        var soniox = new FakeSoniox { PcmBytesPerText = OpusFramePump.FrameByteLength / 2 };
        var client = NewClient(soniox, idleFlush: Long, streamRollover: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s1", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
        text.Writer.TryWrite("Hello,");
        var first = await pcm.Reader.ReadAsync();
        var audioStartsAfterFirst = listener.AudioStarts;
        text.Writer.TryWrite("world.");
        var second = await pcm.Reader.ReadAsync();
        var audioStartsAfterSecond = listener.AudioStarts;
        text.Writer.TryComplete();
        await runTask;

        // assert
        first.Length.Should().BeLessThan(OpusFramePump.FrameByteLength);
        (first.Length + second.Length).Should().Be(OpusFramePump.FrameByteLength);
        listener.StreamsOpened.Should().Be(1);
        audioStartsAfterFirst.Should().Be(0, "a chunk shorter than one frame yields no frame yet");
        audioStartsAfterSecond.Should().Be(1, "the chunk that completes the first frame is when audio starts");
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
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
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

    private sealed class RecordingListener : ISpeechSynthesisListener
    {
        public int StreamsOpened;
        public int AudioStarts;

        public void OnStreamOpened() => StreamsOpened++;
        public void OnAudioStarted() => AudioStarts++;
    }

    // A Soniox stand-in that speaks PcmBytesPerText of silence per text (one 20 ms frame by default)
    // and can kill a stream (408) or drop the connection after a given number of texts on the first
    // connection.
    private sealed class FakeSoniox
    {
        private readonly List<(int Count, TaskCompletionSource Source)> _streamOpenedWaiters = new();

        public int KillAfterTextCount { get; init; }
        public int DropAfterTextCount { get; init; }
        public bool DropEveryConnection { get; init; }
        public int PcmBytesPerText { get; init; } = OpusFramePump.FrameByteLength;
        public int ConnectionCount { get; private set; }
        public int OpenedStreamCount { get; private set; }
        public List<SentMessage> Sent { get; } = new();

        public Task WhenStreamsOpened(int count)
        {
            lock (_streamOpenedWaiters) {
                if (OpenedStreamCount >= count)
                    return Task.CompletedTask;

                var source = TaskCompletionSourceExt.New();
                _streamOpenedWaiters.Add((count, source));
                return source.Task;
            }
        }

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
            var audio = Convert.ToBase64String(new byte[PcmBytesPerText]);
            await foreach (var json in webSocket.Sent.Reader.ReadAllAsync()) {
                var message = JsonDocument.Parse(json).RootElement;
                var streamId = message.GetProperty("stream_id").GetString();
                if (message.TryGetProperty("api_key", out _)) {
                    message.GetProperty("audio_format").GetString().Should().Be("pcm_s16le");
                    message.TryGetProperty("bitrate", out _).Should().BeFalse("PCM has no bitrate");
                    lock (Sent)
                        Sent.Add(new SentMessage($"config {streamId}"));
                    OnStreamOpened();
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
                    webSocket.Incoming.Writer.TryWrite($$"""{"stream_id":"{{streamId}}","audio":"{{audio}}"}""");
                }
                if (isEnd)
                    webSocket.Incoming.Writer.TryWrite($$"""{"stream_id":"{{streamId}}","terminated":true}""");
            }
        }

        private void OnStreamOpened()
        {
            lock (_streamOpenedWaiters) {
                OpenedStreamCount++;
                for (var i = _streamOpenedWaiters.Count - 1; i >= 0; i--) {
                    var (count, source) = _streamOpenedWaiters[i];
                    if (OpenedStreamCount < count)
                        continue;

                    source.TrySetResult();
                    _streamOpenedWaiters.RemoveAt(i);
                }
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
