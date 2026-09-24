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
    public async Task RunShouldEndEachChunkOnItsOwnStream()
    {
        // arrange - the fake holds every terminated for a while, so a chunk sent before the previous
        // stream ended would show up ahead of it in the traffic
        var listener = new RecordingListener();
        var soniox = new FakeSoniox { TerminatedDelay = Short };
        var client = NewClient(soniox, idleFlush: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        foreach (var chunk in new[] { "One.", "Two.", "Three." })
            text.Writer.TryWrite(chunk);
        text.Writer.Complete();

        // act
        await client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(3, "every chunk is its own stream");
        soniox.ConnectionCount.Should().Be(1, "the streams share the connection");
        soniox.Traffic.Should().Equal(
            "config s-1", "text+end s-1 'One.'", "audio s-1", "terminated s-1",
            "config s-2", "text+end s-2 'Two.'", "audio s-2", "terminated s-2",
            "config s-3", "text+end s-3 'Three.'", "audio s-3", "terminated s-3");
        audio.Select(TextOf).Should().Equal("One.", "Two.", "Three.");
        listener.StreamsOpened.Should().Be(3, "the listener sees every stream on its chunk");
        listener.AudioStarts.Should().Be(3, "every stream's first frame is reported");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldReuseTheConnectionAcrossStreams()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        for (var i = 1; i <= 6; i++)
            text.Writer.TryWrite($"Chunk {i}.");
        text.Writer.Complete();

        // act
        await client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(6);
        soniox.ConnectionCount.Should().Be(2, "a connection carries at most 5 streams");
        soniox.StreamConnections.Should().Equal(new Dictionary<string, int> {
            { "s-1", 1 }, { "s-2", 1 }, { "s-3", 1 }, { "s-4", 1 }, { "s-5", 1 }, { "s-6", 2 },
        });
        soniox.Sent.Should().Equal(Enumerable.Range(1, 6)
            .SelectMany(i => new[] { $"config s-{i}", $"text+end s-{i} 'Chunk {i}.'" }));
        audio.Select(TextOf).Should().Equal(Enumerable.Range(1, 6).Select(i => $"Chunk {i}."));
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldEndAnIdleStreamAndOpenANewOneForTheNextChunk()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Short);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act - the pre-opened stream idles out empty before the first chunk arrives, and so does
        // the one pre-opened after it
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
        await Task.Delay(Short * 4);
        text.Writer.TryWrite("First. ");
        await Task.Delay(Short * 4);
        WriteLastChunk(soniox, text.Writer, "Second. ");
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(4,
            "the stream pre-opened at start and the one pre-opened after the first chunk both idle out");
        soniox.ConnectionCount.Should().Be(1, "all streams share the connection");
        soniox.Sent.Should().Equal(
            "config s-1", "end s-1",
            "config s-2", "text+end s-2 'First. '",
            "config s-3", "end s-3",
            "config s-4", "text+end s-4 'Second. '");
        audio.Select(TextOf).Should().Equal("First. ", "Second. ");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldOpenTheStreamBeforeAnyText()
    {
        // arrange
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        var listener = new RecordingListener();

        // act
        var runTask = client.Run("s1", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
        await soniox.WhenStreamsOpened(1).WaitAsync(TimeSpan.FromSeconds(2));
        var openedBeforeText = listener.StreamsOpened;
        WriteLastChunk(soniox, text.Writer, "Hello world. ");
        await runTask;

        // assert
        soniox.OpenedStreamCount.Should().Be(1, "the stream was opened at start and reused for the first chunk");
        openedBeforeText.Should().Be(0, "the listener still sees the stream as opened on its first text");
        listener.StreamsOpened.Should().Be(1);
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldDiscardAudioFromAStreamThatNeverGotText()
    {
        // arrange - Soniox answers text_end on an empty stream with real audio, which is never speech
        var listener = new RecordingListener();
        var soniox = new FakeSoniox { PcmBytesOnEmptyEnd = OpusFramePump.FrameByteLength };
        var client = NewClient(soniox, idleFlush: Short);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act - the pre-opened stream idles out before any text arrives, then a real chunk is sent
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
        await Task.Delay(Short * 2);
        WriteLastChunk(soniox, text.Writer, "Real. ");
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        client.StreamCount.Should().Be(2, "the empty stream idles out before the real chunk arrives");
        audio.Select(TextOf).Should().Equal("Real. ");
        listener.AudioStarts.Should().Be(1, "the discarded empty-stream audio never counts as speech starting");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldResendAnUnspokenChunkAfterSonioxKillsTheStream()
    {
        // arrange
        var listener = new RecordingListener();
        var soniox = new FakeSoniox { KillAfterTextCount = 2 };
        var client = NewClient(soniox, idleFlush: Long);
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
        client.StreamCount.Should().Be(4, "the killed stream is replaced right away, then one is pre-opened");
        soniox.Sent.Should().Equal(
            "config s-1", "text+end s-1 'Spoken. '",
            "config s-2", "text+end s-2 'Lost. '",
            "config s-3", "text+end s-3 'Lost. '",
            "config s-4", "end s-4");
        audio.Select(TextOf).Should().Equal("Spoken. ", "Lost. ");
        listener.StreamsOpened.Should().Be(3, "the replacement stream opens too, the empty pre-opened one doesn't");
        listener.AudioStarts.Should().Be(2, "'Spoken.' answers before the kill, 'Lost.' after the resend");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldReconnectOnceWhenTheConnectionDrops()
    {
        // arrange
        var soniox = new FakeSoniox { DropAfterTextCount = 1 };
        var client = NewClient(soniox, idleFlush: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();

        // act
        var runTask = client.Run("s", "en", "Adrian", text.Reader, pcm.Writer, null, CancellationToken.None);
        text.Writer.TryWrite("Dropped. ");
        await Task.Delay(Short * 2);
        WriteLastChunk(soniox, text.Writer, "Late. ");
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        soniox.ConnectionCount.Should().Be(2);
        client.StreamCount.Should().Be(3);
        soniox.Sent.Should().Equal(
            "config s-1", "text+end s-1 'Dropped. '",
            "config s-2", "text+end s-2 'Dropped. '",
            "config s-3", "text+end s-3 'Late. '");
        audio.Select(TextOf).Should().Equal("Dropped. ", "Late. ");
    }

    [Fact(Timeout = 15_000)]
    public async Task ListenerShouldSeeEachStreamOnceItHasTextAndAudio()
    {
        // arrange - two chunks, the fake answers audio for each
        var listener = new RecordingListener();
        var soniox = new FakeSoniox();
        var client = NewClient(soniox, idleFlush: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        text.Writer.TryWrite("Hello,");
        text.Writer.TryWrite("world.");
        text.Writer.TryComplete();

        // act
        await client.Run("s1", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);

        // assert
        client.StreamCount.Should().Be(2);
        listener.StreamsOpened.Should().Be(2, "opened once per stream, when its chunk was sent");
        listener.AudioStarts.Should().Be(2, "reported once per stream, on its first frame's worth of PCM");
    }

    [Fact(Timeout = 15_000)]
    public async Task ListenerShouldSeeAudioStartOnceAFrameIsComplete()
    {
        // arrange - the fake answers the chunk with two half-frame audio messages
        var soniox = new FakeSoniox {
            PcmBytesPerText = OpusFramePump.FrameByteLength / 2,
            AudioMessagesPerText = 2,
        };
        var client = NewClient(soniox, idleFlush: Long);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        var listener = new RecordingListener(pcm.Reader);
        text.Writer.TryWrite("Hello, world.");
        text.Writer.TryComplete();

        // act
        await client.Run("s1", "en", "Adrian", text.Reader, pcm.Writer, listener, CancellationToken.None);
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        audio.Should().HaveCount(2);
        audio.Should().OnlyContain(chunk => chunk.Length == OpusFramePump.FrameByteLength / 2);
        listener.StreamsOpened.Should().Be(1);
        listener.AudioStarts.Should().Be(1);
        listener.PcmChunksAtAudioStart.Should().Equal([1],
            "a message shorter than one frame yields no frame yet; the one completing the frame is when audio starts");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunShouldFailWhenTheConnectionDropsTwice()
    {
        // arrange
        var soniox = new FakeSoniox { DropAfterTextCount = 1, DropEveryConnection = true };
        var client = NewClient(soniox, idleFlush: Short);
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

    // Writing the last chunk and ending the text are two steps: had the chunk's stream terminated in between,
    // the client would pre-open one more stream for a chunk that never comes
    private static void WriteLastChunk(FakeSoniox soniox, ChannelWriter<string> text, string chunk)
    {
        var terminatedRelease = TaskCompletionSourceExt.New();
        soniox.WhenTerminatedReleased = terminatedRelease.Task;
        text.TryWrite(chunk);
        text.Complete();
        terminatedRelease.SetResult();
    }

    private SonioxTtsClient NewClient(FakeSoniox soniox, TimeSpan idleFlush)
    {
        var services = new ServiceCollection()
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddTestLogging(Out);
        return new SonioxTtsClient(services.BuildServiceProvider()) {
            IdleFlush = idleFlush,
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

    // The fake speaks a chunk as its text padded with zeros to the PCM size
    private static string TextOf(byte[] pcm)
        => Encoding.UTF8.GetString(pcm).TrimEnd('\0');

    // Nested types

    private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond.Invoke(request));
    }

    private sealed class RecordingListener(ChannelReader<byte[]>? pcm = null) : ISpeechSynthesisListener
    {
        public int StreamsOpened;
        public int AudioStarts;
        public List<int> PcmChunksAtAudioStart { get; } = new();

        public void OnStreamOpened() => StreamsOpened++;

        public void OnAudioStarted()
        {
            AudioStarts++;
            if (pcm != null)
                PcmChunksAtAudioStart.Add(pcm.Count);
        }
    }

    // A Soniox stand-in that answers every text with AudioMessagesPerText audio messages of PcmBytesPerText
    // bytes (one 20 ms frame by default) carrying the text itself, and can kill a stream (408) or drop
    // the connection after a given number of texts on the first connection. Traffic lists both what it
    // received and what it wrote, in order; Sent is the received part.
    private sealed class FakeSoniox
    {
        private readonly List<(int Count, TaskCompletionSource Source)> _streamOpenedWaiters = new();
        private readonly HashSet<string> _streamsWithText = new();
        private readonly List<(bool IsFromClient, string Summary)> _traffic = new();
        private readonly Dictionary<string, int> _streamConnections = new();

        public int KillAfterTextCount { get; init; }
        public int DropAfterTextCount { get; init; }
        public bool DropEveryConnection { get; init; }
        public int PcmBytesPerText { get; init; } = OpusFramePump.FrameByteLength;
        public int AudioMessagesPerText { get; init; } = 1;
        public int PcmBytesOnEmptyEnd { get; init; }
        public TimeSpan TerminatedDelay { get; init; }
        public Task WhenTerminatedReleased { get; set; } = Task.CompletedTask;
        public int ConnectionCount { get; private set; }
        public int OpenedStreamCount { get; private set; }

        public IReadOnlyList<string> Traffic {
            get {
                lock (_traffic)
                    return _traffic.Select(x => x.Summary).ToList();
            }
        }

        public IReadOnlyList<string> Sent {
            get {
                lock (_traffic)
                    return _traffic.Where(x => x.IsFromClient).Select(x => x.Summary).ToList();
            }
        }

        public IReadOnlyDictionary<string, int> StreamConnections {
            get {
                lock (_streamConnections)
                    return new Dictionary<string, int>(_streamConnections);
            }
        }

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
            _ = Serve(webSocket, ConnectionCount, ConnectionCount == 1 || DropEveryConnection);
            return Task.FromResult<WebSocket>(webSocket);
        }

        private async Task Serve(FakeWebSocket webSocket, int connectionIndex, bool mayFail)
        {
            var textCount = 0;
            await foreach (var json in webSocket.Sent.Reader.ReadAllAsync()) {
                var message = JsonDocument.Parse(json).RootElement;
                var streamId = message.GetProperty("stream_id").GetString()!;
                if (message.TryGetProperty("api_key", out _)) {
                    message.GetProperty("audio_format").GetString().Should().Be("pcm_s16le");
                    message.TryGetProperty("bitrate", out _).Should().BeFalse("PCM has no bitrate");
                    Record(true, $"config {streamId}");
                    lock (_streamConnections)
                        _streamConnections[streamId] = connectionIndex;
                    OnStreamOpened();
                    continue;
                }

                var text = message.GetProperty("text").GetString() ?? "";
                var isEnd = message.GetProperty("text_end").GetBoolean();
                Record(true, (text.Length, isEnd) switch {
                    (0, _) => $"end {streamId}",
                    (_, true) => $"text+end {streamId} '{text}'",
                    (_, false) => $"text {streamId} '{text}'",
                });
                if (text.Length > 0) {
                    textCount++;
                    lock (_streamsWithText)
                        _streamsWithText.Add(streamId);
                    if (mayFail && textCount == DropAfterTextCount) {
                        webSocket.Incoming.Writer.TryComplete();
                        return;
                    }
                    if (mayFail && textCount == KillAfterTextCount) {
                        Record(false, $"killed {streamId}");
                        webSocket.Incoming.Writer.TryWrite(
                            $$"""{"stream_id":"{{streamId}}","error_code":408,"error_message":"Stream killed"}""");
                        continue;
                    }
                    var audio = new byte[PcmBytesPerText];
                    Encoding.UTF8.GetBytes(text, audio);
                    for (var i = 0; i < AudioMessagesPerText; i++)
                        WriteAudio(webSocket, streamId, audio);
                }
                if (isEnd) {
                    bool hadText;
                    lock (_streamsWithText)
                        hadText = _streamsWithText.Remove(streamId);
                    if (!hadText && PcmBytesOnEmptyEnd > 0)
                        WriteAudio(webSocket, streamId, new byte[PcmBytesOnEmptyEnd]);
                    _ = Terminate(webSocket, streamId);
                }
            }
        }

        private void WriteAudio(FakeWebSocket webSocket, string streamId, byte[] audio)
        {
            Record(false, $"audio {streamId}");
            var base64 = Convert.ToBase64String(audio);
            webSocket.Incoming.Writer.TryWrite($$"""{"stream_id":"{{streamId}}","audio":"{{base64}}"}""");
        }

        private async Task Terminate(FakeWebSocket webSocket, string streamId)
        {
            await WhenTerminatedReleased;
            if (TerminatedDelay > TimeSpan.Zero)
                await Task.Delay(TerminatedDelay);
            Record(false, $"terminated {streamId}");
            webSocket.Incoming.Writer.TryWrite($$"""{"stream_id":"{{streamId}}","terminated":true}""");
        }

        private void Record(bool isFromClient, string summary)
        {
            lock (_traffic)
                _traffic.Add((isFromClient, summary));
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
