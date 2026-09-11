using System.Net.WebSockets;
using System.Text;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Soniox;

namespace ActualChat.Transcription;

/// <summary>
/// One real-time session of Soniox's <c>tts-rt</c> WebSocket API: text chunks in, 48 kHz PCM out.
/// A session spans several Soniox streams, each capped at <see cref="Options.MaxStreamDuration"/>.
/// </summary>
public sealed class SonioxTtsClient(IServiceProvider services, SonioxTtsClient.Options? options = null)
{
    public sealed record Options
    {
        public TimeSpan MaxStreamDuration { get; init; } = TtsMaxStreamDuration;
    }

    private const string Url = "wss://tts-rt.soniox.com/tts-websocket";
    private const string Model = "tts-rt-v2";
    private const string PcmFormat = "pcm_s16le";
    private const int SampleRate = 48_000;
    private const int BytesPerSecond = SampleRate * sizeof(short);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly byte[] KeepAlivePayload = """{"keep_alive":true}"""u8.ToArray();

    private readonly ConcurrentDictionary<string, StreamState> _streams = new();
    private string? _finalStreamId;

    private CoreServerSettings CoreServerSettings { get; } = services.GetRequiredService<CoreServerSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<SonioxTtsClient>();

    public Options Settings { get; } = options ?? new();
    public int StreamCount { get; private set; }

    public async Task Run(
        string sessionId,
        string language,
        string voice,
        ChannelReader<string> text,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        Exception? error = null;
        using var webSocket = new ClientWebSocket();
        using var sender = new SonioxSocketSender(webSocket, Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? keepAliveTask = null;
        try {
            await webSocket.ConnectAsync(new Uri(Url), cancellationToken).ConfigureAwait(false);
            var config = new StreamConfig(sessionId, apiKey, language, voice);
            keepAliveTask = KeepAlive(sender, cts.Token);
            await TranscriberHelper.WhenPushAndRead(
                    PushText(sender, config, text, cts.Token),
                    ReadAudio(webSocket, sessionId, pcm, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox TTS failed for #{SessionId}", sessionId);
            throw;
        }
        finally {
            await cts.CancelAsync().ConfigureAwait(false);
            if (keepAliveTask != null)
                await keepAliveTask.SilentAwait(false);
            pcm.TryComplete(error);
        }
    }

    // Private methods

    private async Task PushText(
        SonioxSocketSender sender,
        StreamConfig config,
        ChannelReader<string> text,
        CancellationToken cancellationToken)
    {
        // The first stream opens before any text exists: the config has to reach the server within
        // ~10s of connecting, and the first translated sentence can take longer than that.
        var streamId = await StartStream(sender, config, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            if (chunk.IsNullOrWhiteSpace())
                continue;

            if (_streams[streamId].GeneratedDuration >= Settings.MaxStreamDuration) {
                await EndStream(sender, streamId, cancellationToken).ConfigureAwait(false);
                streamId = await StartStream(sender, config, cancellationToken).ConfigureAwait(false);
            }
            await Send(sender, new { text = chunk, text_end = false, stream_id = streamId }, cancellationToken)
                .ConfigureAwait(false);
        }

        Volatile.Write(ref _finalStreamId, streamId);
        await EndStream(sender, streamId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> StartStream(
        SonioxSocketSender sender,
        StreamConfig config,
        CancellationToken cancellationToken)
    {
        var streamId = $"{config.SessionId}-{++StreamCount}";
        _streams[streamId] = new StreamState();
        var message = new Dictionary<string, object?> {
            ["api_key"] = config.ApiKey,
            ["model"] = Model,
            ["language"] = config.Language,
            ["voice"] = config.Voice,
            ["audio_format"] = PcmFormat,
            ["sample_rate"] = SampleRate,
            ["stream_id"] = streamId,
        };
        await Send(sender, message, cancellationToken).ConfigureAwait(false);
        return streamId;
    }

    private async Task EndStream(SonioxSocketSender sender, string streamId, CancellationToken cancellationToken)
    {
        await Send(sender, new { text = "", text_end = true, stream_id = streamId }, cancellationToken)
            .ConfigureAwait(false);
        await _streams[streamId].WhenTerminated.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task Send(SonioxSocketSender sender, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return sender.Send(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, cancellationToken);
    }

    private async Task KeepAlive(SonioxSocketSender sender, CancellationToken cancellationToken)
    {
        var clock = Clocks.CpuClock;
        while (true) {
            var delay = TtsKeepAlivePeriod - (clock.Now - sender.LastSendAt);
            if (delay > TimeSpan.Zero) {
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await sender.Send(KeepAlivePayload, WebSocketMessageType.Text, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadAudio(
        ClientWebSocket webSocket,
        string sessionId,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        var message = new StringBuilder();
        while (webSocket.State == WebSocketState.Open) {
            message.Clear();
            WebSocketReceiveResult result;
            do {
                result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                message.Append(Encoding.UTF8.GetString(buffer.Array!, 0, result.Count));
            } while (!result.EndOfMessage);

            var response = JsonSerializer.Deserialize<SonioxTtsResponse>(message.ToString(), JsonOptions);
            if (response == null)
                continue;
            if (response.ErrorCode is { } errorCode)
                throw StandardError.External(
                    $"Soniox TTS error {errorCode} for #{sessionId}: {response.ErrorMessage}");

            var streamId = response.StreamId ?? "";
            if (!response.Audio.IsNullOrEmpty()) {
                var bytes = Convert.FromBase64String(response.Audio);
                if (_streams.TryGetValue(streamId, out var state))
                    state.GeneratedDuration += TimeSpan.FromSeconds(bytes.Length / (double)BytesPerSecond);
                await pcm.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            if (response.Terminated) {
                if (_streams.TryGetValue(streamId, out var state))
                    state.WhenTerminated.TrySetResult();
                if (streamId == Volatile.Read(ref _finalStreamId))
                    return;
            }
        }
    }

    // Nested types

    private sealed record StreamConfig(string SessionId, string ApiKey, string Language, string Voice);

    private sealed class StreamState
    {
        public TaskCompletionSource WhenTerminated { get; } = TaskCompletionSourceExt.New();
        public TimeSpan GeneratedDuration { get; set; }
    }
}
