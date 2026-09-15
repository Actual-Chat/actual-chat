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
/// One utterance over Soniox's <c>tts-rt</c> WebSocket API: text chunks in, 20 ms Opus frames out (Soniox
/// sends Ogg/Opus; every stream is parsed by its own <see cref="OggOpusReader"/>, frame offsets run
/// contiguously across streams). The whole run shares one connection and, as far as Soniox allows, one
/// stream, so sentences keep their prosody across chunks. A stream is ended early only when the text goes
/// idle (Soniox kills a stream that produces nothing for a few seconds and loses its unsynthesized text)
/// or when it nears Soniox's 2-minute stream cap; the next chunk then opens a new stream on the same
/// connection.
/// </summary>
public sealed class SonioxTtsClient(IServiceProvider services)
{
    public const string HttpClientName = nameof(SonioxTtsClient);
    private const string Url = "wss://tts-rt.soniox.com/tts-websocket";
    private const string RestUrl = "https://tts-rt.soniox.com/tts";
    private const string Model = "tts-rt-v2";
    private const string OpusFormat = "opus";
    private const string Mp3Format = "mp3";
    private const int SampleRate = 48_000;
    // What every other Opus stream here is encoded at; Soniox's default is ~80 kbps
    private const int OpusBitrate = Constants.Audio.Bitrate;
    private const int MaxTextLength = 5000;
    private const int ReadBufferSize = 32 * 1024;
    private const int MaxStreamsPerConnection = 5;
    private const int StreamKilledErrorCode = 408;
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
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        _run = new RunArgs(sessionId, apiKey, language, voice, output);
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
            output.TryComplete(error);
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
        if (_chunksToResend.Count > 0)
            await OpenStream(cancellationToken).ConfigureAwait(false);
        while (true) {
            _readTask ??= ReadChunk(text, cancellationToken);
            if (_stream != null && !_readTask.IsCompleted) {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var idleTask = Task.Delay(IdleFlush, idleCts.Token);
                var first = await Task.WhenAny(_readTask, idleTask, _stream.WhenTerminated).ConfigureAwait(false);
                await idleCts.CancelAsync().ConfigureAwait(false);
                if (first == idleTask) {
                    await EndStream(cancellationToken).ConfigureAwait(false);
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
                    await EndStream(cancellationToken).ConfigureAwait(false);
                else
                    ThrowIfSilent(_stream);
            }
            if (_stream == null)
                await OpenStream(cancellationToken).ConfigureAwait(false);
            await SendChunk(chunk, false, cancellationToken).ConfigureAwait(false);
        }
        while (_stream != null)
            await EndStream(cancellationToken).ConfigureAwait(false);
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
            audio_format = OpusFormat,
            sample_rate = SampleRate,
            bitrate = OpusBitrate,
            stream_id = stream.Id,
        }, cancellationToken).ConfigureAwait(false);
        while (_chunksToResend.Count > 0) {
            await SendChunk(_chunksToResend[0], true, cancellationToken).ConfigureAwait(false);
            _chunksToResend.RemoveAt(0);
        }
    }

    private async Task EndStream(CancellationToken cancellationToken)
    {
        var stream = _stream!;
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
        if (stream.Reader.HasPendingData)
            Log.LogWarning("Soniox TTS stream #{StreamId} ended in the middle of an Ogg page", stream.Id);
        if (response.ErrorCode is not { } errorCode)
            return;

        Log.LogWarning("Soniox TTS stream #{StreamId} ended with error {ErrorCode} ({ErrorType}): {ErrorMessage}",
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
        var output = _run!.Output;
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
                    stream.OnAudioReceived();
                    var audio = Convert.FromBase64String(response.Audio);
                    await WriteFrames(stream.Reader, audio, output, cancellationToken).ConfigureAwait(false);
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
        _stream!.OnChunkSent(chunk, isResent);
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

    // Nested types

    private sealed record RunArgs(
        string SessionId,
        string ApiKey,
        string Language,
        string Voice,
        ChannelWriter<AudioFrame> Output);

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

        public string Id { get; } = id;
        public Moment StartedAt { get; } = startedAt;
        public Moment LastMessageAt { get; set; } = startedAt;
        // Every Soniox stream is an Ogg/Opus stream of its own, headers included
        public OggOpusReader Reader { get; } = new();
        // The result is the message that ended the stream (an error or terminated);
        // the task faults when the connection died under the stream.
        public Task<SonioxTtsResponse> WhenTerminated => _whenTerminatedSource.Task;

        public void OnChunkSent(string chunk, bool isResent)
        {
            lock (_unspokenChunks)
                _unspokenChunks.Add((chunk, isResent));
        }

        // Soniox synthesizes a sentence only once it sees the text after it, so the chunks sent
        // since the last audio are the closest cheap guess at what a killed stream never spoke
        public void OnAudioReceived()
        {
            lock (_unspokenChunks)
                _unspokenChunks.Clear();
        }

        // A chunk is resent at most once, so a stream that keeps dying can't loop
        public List<string> TakeUnspokenChunks()
        {
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
