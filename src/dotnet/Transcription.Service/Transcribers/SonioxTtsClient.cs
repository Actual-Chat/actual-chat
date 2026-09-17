using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using ActualChat.Audio;
using ActualChat.Audio.Ogg;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Soniox;

namespace ActualChat.Transcription;

/// <summary>
/// One utterance over Soniox's <c>tts-rt</c> WebSocket API: text chunks in, 48 kHz PCM out. The whole run
/// shares one connection and, as far as Soniox allows, one stream, so sentences keep their prosody across
/// chunks. A stream is ended early only when the text goes idle (Soniox kills a stream that produces nothing
/// for a few seconds and loses its unsynthesized text) or when it nears Soniox's 2-minute stream cap; the
/// next chunk then opens a new stream on the same connection.
/// </summary>
public sealed class SonioxTtsClient(IServiceProvider services)
{
    public const string HttpClientName = nameof(SonioxTtsClient);
    private const string Url = "wss://tts-rt.soniox.com/tts-websocket";
    private const string RestUrl = "https://tts-rt.soniox.com/tts";
    private const string Model = "tts-rt-v2";
    // Live streams take PCM: Soniox pages its Opus per second of audio, which puts the first frame of every
    // stream ~1 s later than PCM's 256 ms chunks, and the voice-over mixer needs PCM anyway
    private const string PcmFormat = "pcm_s16le";
    private const string OpusFormat = "opus";
    private const string Mp3Format = "mp3";
    private const int SampleRate = 48_000;
    // What every other Opus stream here is encoded at; Soniox's default is ~80 kbps
    private const int OpusBitrate = Constants.Audio.Bitrate;
    private const int MaxTextLength = 5000;
    private const int ReadBufferSize = 32 * 1024;
    private const int MaxStreamsPerConnection = 5;
    private const int StreamKilledErrorCode = 408;
    private const string ClauseEnds = ".!?…,;:。！？，；：";
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<string> _chunksToResend = new();
    private RunArgs? _run;
    private Connection? _connection;
    private TtsStream? _stream;
    private Task<string?>? _readTask;
    private int _frameCount;

    private IServiceProvider Services { get; } = services;
    private CoreServerSettings CoreServerSettings { get; } = services.GetRequiredService<CoreServerSettings>();
    private IHttpClientFactory HttpClientFactory => field ??= Services.HttpClientFactory();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<SonioxTtsClient>();
    private Moment Now => Clocks.CpuClock.Now;

    public int StreamCount { get; private set; }

    // Test hooks
    internal TimeSpan IdleFlush { get; init; } = TtsIdleFlush;
    internal TimeSpan StreamRollover { get; init; } = TtsStreamRollover;
    internal Func<CancellationToken, Task<WebSocket>> WebSocketFactory { get; init; } = ConnectToSoniox;

    public async Task Run(
        string sessionId,
        string language,
        string voice,
        ChannelReader<string> text,
        ChannelWriter<byte[]> pcm,
        ISpeechSynthesisListener? listener,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        _run = new RunArgs(sessionId, apiKey, language, voice, pcm, listener);
        Exception? error = null;
        try {
            var hasReconnected = false;
            while (true) {
                try {
                    await RunStreams(text, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception e) when (IsConnectionFailure(e)
                    && !hasReconnected
                    && !cancellationToken.IsCancellationRequested) {
                    hasReconnected = true;
                    Log.LogWarning(e, "Soniox TTS connection lost for #{SessionId}, reconnecting", sessionId);
                    await OnConnectionLost().ConfigureAwait(false);
                }
            }
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox TTS failed for #{SessionId}", sessionId);
            throw;
        }
        finally {
            await CloseConnection().ConfigureAwait(false);
            pcm.TryComplete(error);
        }
    }

    public async Task Generate(
        string language,
        string voice,
        string text,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        Exception? error = null;
        try {
            using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            foreach (var part in SplitText(text, MaxTextLength)) {
                // Every part is a complete Ogg/Opus stream of its own
                var reader = new OggOpusReader();
                await GeneratePart(
                        httpClient,
                        language,
                        voice,
                        part,
                        OpusFormat,
                        chunk => WriteFrames(reader, chunk, output, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (reader.HasPendingData)
                    throw StandardError.Format("Soniox TTS response ended in the middle of an Ogg page.");
            }
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox TTS generation failed");
            throw;
        }
        finally {
            output.TryComplete(error);
        }
    }

    public async Task<byte[]> GenerateMp3(
        string language,
        string voice,
        string text,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        // One request: a preview sentence is far below MaxTextLength, and MP3 parts can't be concatenated
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text), $"Text is longer than {MaxTextLength} characters.");

        using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var mp3 = new MemoryStream();
        try {
            await GeneratePart(
                    httpClient,
                    language,
                    voice,
                    text,
                    Mp3Format,
                    chunk => {
                        mp3.Write(chunk.Span);
                        return default;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Soniox TTS MP3 generation failed");
            throw;
        }

        return mp3.ToArray();
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal static IEnumerable<string> SplitText(string text, int maxLength)
    {
        var start = 0;
        while (text.Length - start > maxLength) {
            var end = text.LastIndexOfAny(['.', '!', '?', '\n'], start + maxLength - 1, maxLength);
            if (end <= start)
                end = text.LastIndexOf(' ', start + maxLength - 1, maxLength);
            if (end <= start)
                end = start + maxLength - 1;
            yield return text[start..(end + 1)];
            start = end + 1;
        }
        if (start < text.Length)
            yield return text[start..];
    }

    // Private methods

    private async Task RunStreams(ChannelReader<string> text, CancellationToken cancellationToken)
    {
        // Opened before the first chunk: the connect + open (~0.4 s) overlaps the first clause's
        // translation instead of following it. Idle streams re-open on their next chunk below.
        if (_stream == null)
            await OpenStream(cancellationToken).ConfigureAwait(false);
        while (true) {
            _readTask ??= ReadChunk(text, cancellationToken);
            if (_stream != null && !_readTask.IsCompleted) {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var idleTask = Task.Delay(IdleFlush, idleCts.Token);
                var first = await Task.WhenAny(_readTask, idleTask, _stream.WhenTerminated).ConfigureAwait(false);
                await idleCts.CancelAsync().ConfigureAwait(false);
                if (first == idleTask) {
                    await EndStream(StreamEndReason.Idle, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (first != _readTask) {
                    await OnStreamTerminated(cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            var chunk = await _readTask.ConfigureAwait(false);
            _readTask = null;
            if (chunk == null)
                break;

            if (_stream != null) {
                if (_stream.WhenTerminated.IsCompleted)
                    await OnStreamTerminated(cancellationToken).ConfigureAwait(false);
                else if (Now - _stream.StartedAt >= StreamRollover)
                    await EndStream(StreamEndReason.Rollover, cancellationToken).ConfigureAwait(false);
                else
                    ThrowIfSilent(_stream);
            }
            if (_stream == null)
                await OpenStream(cancellationToken).ConfigureAwait(false);
            await SendChunk(chunk, false, cancellationToken).ConfigureAwait(false);
        }
        while (_stream != null)
            await EndStream(StreamEndReason.Final, cancellationToken).ConfigureAwait(false);
    }

    private async Task OpenStream(CancellationToken cancellationToken)
    {
        if (_connection is not { IsOpen: true } || _connection.StreamCount >= MaxStreamsPerConnection) {
            await CloseConnection().ConfigureAwait(false);
            _connection = await Connect(cancellationToken).ConfigureAwait(false);
        }
        var run = _run!;
        var stream = new TtsStream($"{run.SessionId}-{++StreamCount}", Now);
        _stream = stream;
        _connection.OpenStream(stream);
        await Send(new {
            api_key = run.ApiKey,
            model = Model,
            language = run.Language,
            voice = run.Voice,
            audio_format = PcmFormat,
            sample_rate = SampleRate,
            stream_id = stream.Id,
        }, cancellationToken).ConfigureAwait(false);
        Log.LogDebug("Soniox TTS #{StreamId}: opened, stream {Index} of {Max} on its connection",
            stream.Id, _connection.StreamCount, MaxStreamsPerConnection);
        while (_chunksToResend.Count > 0) {
            await SendChunk(_chunksToResend[0], true, cancellationToken).ConfigureAwait(false);
            _chunksToResend.RemoveAt(0);
        }
    }

    private async Task EndStream(StreamEndReason reason, CancellationToken cancellationToken)
    {
        var stream = _stream!;
        Log.LogDebug("Soniox TTS #{StreamId}: ending ({Reason}) {Elapsed:F1}s after it opened",
            stream.Id, reason, (Now - stream.StartedAt).TotalSeconds);
        await SendText("", true, cancellationToken).ConfigureAwait(false);
        while (!stream.WhenTerminated.IsCompleted) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfSilent(stream);
            var timeout = stream.LastMessageAt + TtsChunkTimeout - Now;
            await stream.WhenTerminated.WaitAsync(timeout, cancellationToken).SilentAwait(false);
        }
        await OnStreamTerminated(cancellationToken).ConfigureAwait(false);
    }

    private async Task OnStreamTerminated(CancellationToken cancellationToken)
    {
        var stream = _stream!;
        var response = await stream.WhenTerminated.ConfigureAwait(false);
        _stream = null;
        if (response.ErrorCode is not { } errorCode) {
            Log.LogDebug("Soniox TTS #{StreamId}: terminated {Elapsed:F1}s after it opened",
                stream.Id, (Now - stream.StartedAt).TotalSeconds);
            return;
        }

        Log.LogWarning("Soniox TTS #{StreamId}: ended with error {ErrorCode} ({ErrorType}): {ErrorMessage}",
            stream.Id, errorCode, response.ErrorType, response.ErrorMessage);
        if (errorCode != StreamKilledErrorCode)
            return;

        _chunksToResend.AddRange(stream.TakeUnspokenChunks());
        if (_chunksToResend.Count > 0)
            await OpenStream(cancellationToken).ConfigureAwait(false);
    }

    private async Task OnConnectionLost()
    {
        if (_stream is { } stream) {
            _chunksToResend.AddRange(stream.TakeUnspokenChunks());
            _stream = null;
        }
        await CloseConnection().ConfigureAwait(false);
    }

    private async Task<Connection> Connect(CancellationToken cancellationToken)
    {
        var webSocket = await WebSocketFactory.Invoke(cancellationToken).ConfigureAwait(false);
        var connection = new Connection(webSocket);
        connection.ReaderTask = ReadMessages(connection, cancellationToken);
        return connection;
    }

    private async Task CloseConnection()
    {
        if (_connection is not { } connection)
            return;

        _connection = null;
        var webSocket = connection.WebSocket;
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                .WaitAsync(CloseTimeout)
                .SilentAwait(false);
        await connection.ReaderTask.WaitAsync(CloseTimeout).SilentAwait(false);
        webSocket.Dispose();
        await connection.ReaderTask.SilentAwait(false);
    }

    private async Task ReadMessages(Connection connection, CancellationToken cancellationToken)
    {
        var run = _run!;
        var buffer = new ArraySegment<byte>(new byte[2 * ReadBufferSize]);
        try {
            while (true) {
                var message = await ReceiveMessage(connection.WebSocket, buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (message == null) {
                    connection.Stream?.Fail(new WebSocketException(
                        WebSocketError.ConnectionClosedPrematurely, "Soniox TTS closed the connection."));
                    return;
                }

                var response = JsonSerializer.Deserialize<SonioxTtsResponse>(message, JsonOptions);
                if (response == null)
                    continue;

                // Only terminated and error messages carry a stream_id; audio comes without one
                var stream = connection.Stream;
                var isForStream = stream != null
                    && (response.StreamId == null || response.StreamId == stream.Id);
                if (!isForStream) {
                    if (response.ErrorCode is { } errorCode)
                        Log.LogWarning("Soniox TTS error {ErrorCode} ({ErrorType}) for #{StreamId}: {ErrorMessage}",
                            errorCode, response.ErrorType, response.StreamId, response.ErrorMessage);
                    continue;
                }

                stream!.LastMessageAt = Now;
                if (response.ErrorCode != null) {
                    stream.Terminate(response);
                    continue;
                }

                if (!response.Audio.IsNullOrEmpty()) {
                    if (!stream.HasText) {
                        // Soniox answers text_end on an empty stream with real audio; a stream that never
                        // got a chunk never spoke, so this is never real speech
                        if (stream.TryLogAudioDiscarded())
                            Log.LogDebug("Soniox TTS #{StreamId}: discarding audio - it never got any text",
                                stream.Id);
                    }
                    else {
                        stream.OnAudioReceived();
                        var audio = Convert.FromBase64String(response.Audio);
                        if (stream.TrySignalFirstFrame(audio.Length)) {
                            Log.LogDebug("Soniox TTS #{StreamId}: first audio +{Delay:F1}s after the first chunk",
                                stream.Id, (Now - stream.FirstTextAt).TotalSeconds);
                            run.Listener?.OnAudioStarted();
                        }
                        await run.Pcm.WriteAsync(audio, cancellationToken).ConfigureAwait(false);
                    }
                }
                if (response.Terminated)
                    stream.Terminate(response);
            }
        }
        catch (Exception e) {
            connection.Stream?.Fail(e);
        }
    }

    private Task SendChunk(string chunk, bool isResent, CancellationToken cancellationToken)
    {
        // Chunks are transcript increments that may stop right before the next word,
        // and Soniox tokenizes on whitespace
        if (!char.IsWhiteSpace(chunk[^1]))
            chunk += " ";
        var isFirst = !_stream!.HasText;
        _stream.OnChunkSent(chunk, isResent, Now);
        if (isFirst)
            _run!.Listener?.OnStreamOpened();
        Log.LogDebug("Soniox TTS #{StreamId}: {Action} {Length} chars, {Ending}",
            _stream.Id, isResent ? "resent" : "sent", chunk.Length,
            EndsWithClauseBoundary(chunk) ? "clause-complete" : "mid-clause");
        return SendText(chunk, false, cancellationToken);
    }

    private Task SendText(string text, bool isEnd, CancellationToken cancellationToken)
        => Send(new { text, text_end = isEnd, stream_id = _stream!.Id }, cancellationToken);

    private Task Send(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return _connection!.WebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private void ThrowIfSilent(TtsStream stream)
    {
        if (Now - stream.LastMessageAt >= TtsChunkTimeout)
            throw StandardError.External($"Soniox TTS sent nothing for #{stream.Id} within {TtsChunkTimeout}.");
    }

    private async ValueTask WriteFrames(
        OggOpusReader reader,
        ReadOnlyMemory<byte> chunk,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken)
    {
        // Every part's reader starts its offsets from zero; the output's run on across parts
        reader.Append(chunk.Span);
        while (reader.TryRead(out var frame))
            await output.WriteAsync(new AudioFrame {
                    Data = frame.Data,
                    Offset = Constants.Audio.OpusFrameDuration * _frameCount++,
                }, cancellationToken)
                .ConfigureAwait(false);
    }

    private static async Task GeneratePart(
        HttpClient httpClient,
        string language,
        string voice,
        string part,
        string audioFormat,
        Func<ReadOnlyMemory<byte>, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        // Soniox streams the audio back at about the pace it's spoken, so TtsChunkTimeout bounds the
        // wait for the next piece of the body rather than the whole request
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TtsChunkTimeout);
        try {
            using var request = new HttpRequestMessage(HttpMethod.Post, RestUrl) {
                Content = JsonContent.Create(new {
                    model = Model,
                    language,
                    voice,
                    audio_format = audioFormat,
                    sample_rate = SampleRate,
                    bitrate = audioFormat == OpusFormat ? OpusBitrate : (int?)null,
                    text = part,
                }, options: JsonOptions),
            };
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                throw StandardError.External($"Soniox TTS returned {(int)response.StatusCode}: {body}");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var buffer = new byte[ReadBufferSize];
            while (true) {
                var count = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                if (count == 0)
                    break;

                cts.CancelAfter(TtsChunkTimeout);
                await onChunk.Invoke(buffer.AsMemory(0, count)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw StandardError.External($"Soniox TTS sent no audio for {TtsChunkTimeout}.");
        }
    }

    private static async Task<string?> ReadChunk(ChannelReader<string> text, CancellationToken cancellationToken)
    {
        while (await text.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (text.TryRead(out var chunk))
                if (!chunk.IsNullOrWhiteSpace())
                    return chunk;

        return null;
    }

    private static async Task<WebSocket> ConnectToSoniox(CancellationToken cancellationToken)
    {
        var webSocket = new ClientWebSocket();
        try {
            await webSocket.ConnectAsync(new Uri(Url), cancellationToken).ConfigureAwait(false);
            return webSocket;
        }
        catch {
            webSocket.Dispose();
            throw;
        }
    }

    private static async Task<string?> ReceiveMessage(
        WebSocket webSocket,
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        var message = new StringBuilder();
        WebSocketReceiveResult result;
        do {
            result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            message.Append(Encoding.UTF8.GetString(buffer.Array!, 0, result.Count));
        } while (!result.EndOfMessage);
        return message.ToString();
    }

    private static bool IsConnectionFailure(Exception e)
        => e is WebSocketException or IOException or SocketException;

    private static bool EndsWithClauseBoundary(string chunk)
    {
        // Soniox speaks up to the last clause boundary at once and holds the rest for lookahead
        var text = chunk.AsSpan().TrimEnd();
        return text.Length > 0 && ClauseEnds.Contains(text[^1]);
    }

    // Nested types

    private enum StreamEndReason
    {
        Final,
        Idle,
        Rollover,
    }

    private sealed record RunArgs(
        string SessionId,
        string ApiKey,
        string Language,
        string Voice,
        ChannelWriter<byte[]> Pcm,
        ISpeechSynthesisListener? Listener);

    private sealed class Connection(WebSocket webSocket)
    {
        public WebSocket WebSocket { get; } = webSocket;
        public Task ReaderTask { get; set; } = Task.CompletedTask;
        public TtsStream? Stream { get; private set; }
        public int StreamCount { get; private set; }
        public bool IsOpen => WebSocket.State == WebSocketState.Open && !ReaderTask.IsCompleted;

        public void OpenStream(TtsStream stream)
        {
            Stream = stream;
            StreamCount++;
        }
    }

    private sealed class TtsStream(string id, Moment startedAt)
    {
        private readonly TaskCompletionSource<SonioxTtsResponse> _whenTerminatedSource
            = TaskCompletionSourceExt.New<SonioxTtsResponse>();
        private readonly List<(string Chunk, bool IsResent)> _unspokenChunks = new();
        private int _pcmByteCount;
        private bool _hasLoggedDiscardedAudio;

        public string Id { get; } = id;
        public Moment StartedAt { get; } = startedAt;
        public Moment FirstTextAt { get; private set; } = startedAt;
        public Moment LastMessageAt { get; set; } = startedAt;
        // The result is the message that ended the stream (an error or terminated);
        // the task faults when the connection died under the stream.
        public Task<SonioxTtsResponse> WhenTerminated => _whenTerminatedSource.Task;
        public bool HasText { get; private set; }
        public bool HasFrames { get; private set; }

        public void OnChunkSent(string chunk, bool isResent, Moment now)
        {
            if (!HasText)
                FirstTextAt = now;
            HasText = true;
            lock (_unspokenChunks)
                _unspokenChunks.Add((chunk, isResent));
        }

        public bool TrySignalFirstFrame(int pcmByteCount)
        {
            // True once per stream, on the chunk that completes its first 20 ms frame: a shorter first
            // chunk encodes to nothing yet, so this is when Soniox actually starts speaking
            if (HasFrames)
                return false;

            _pcmByteCount += pcmByteCount;
            if (_pcmByteCount < OpusFramePump.FrameByteLength)
                return false;

            HasFrames = true;
            return true;
        }

        public bool TryLogAudioDiscarded()
        {
            if (_hasLoggedDiscardedAudio)
                return false;

            _hasLoggedDiscardedAudio = true;
            return true;
        }

        public void OnAudioReceived()
        {
            // Soniox synthesizes a sentence only once it sees the text after it, so the chunks sent
            // since the last audio are the closest cheap guess at what a killed stream never spoke
            lock (_unspokenChunks)
                _unspokenChunks.Clear();
        }

        public List<string> TakeUnspokenChunks()
        {
            // A chunk is resent at most once, so a stream that keeps dying can't loop
            lock (_unspokenChunks) {
                var chunks = _unspokenChunks.Where(x => !x.IsResent).Select(x => x.Chunk).ToList();
                _unspokenChunks.Clear();
                return chunks;
            }
        }

        public void Terminate(SonioxTtsResponse response)
            => _whenTerminatedSource.TrySetResult(response);

        public void Fail(Exception error)
            => _whenTerminatedSource.TrySetException(error);
    }
}
