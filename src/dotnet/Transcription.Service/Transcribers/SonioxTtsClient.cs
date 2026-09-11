using System.Net.WebSockets;
using System.Text;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Soniox;

namespace ActualChat.Transcription;

/// <summary>
/// One real-time session of Soniox's <c>tts-rt</c> WebSocket API: text chunks in, 48 kHz PCM out.
/// Each chunk gets its own stream, opened and closed around just that chunk - Soniox holds
/// synthesis until it sees <c>text_end</c>, and 408s a stream left open with no more text coming.
/// </summary>
public sealed class SonioxTtsClient(IServiceProvider services)
{
    private const string Url = "wss://tts-rt.soniox.com/tts-websocket";
    private const string Model = "tts-rt-v2";
    private const string PcmFormat = "pcm_s16le";
    private const int SampleRate = 48_000;
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly byte[] KeepAlivePayload = """{"keep_alive":true}"""u8.ToArray();

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _whenStreamTerminated = new();
    private readonly TaskCompletionSource _whenDone = TaskCompletionSourceExt.New();

    private CoreServerSettings CoreServerSettings { get; } = services.GetRequiredService<CoreServerSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<SonioxTtsClient>();

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
        await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            if (chunk.IsNullOrWhiteSpace())
                continue;

            var streamId = await StartStream(sender, config, cancellationToken).ConfigureAwait(false);
            await Send(sender, new { text = chunk, text_end = true, stream_id = streamId }, cancellationToken)
                .ConfigureAwait(false);
            await _whenStreamTerminated[streamId].Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            _whenStreamTerminated.TryRemove(streamId, out _);
        }

        // No more chunks are coming - ReadAudio would otherwise wait forever for the next message.
        _whenDone.TrySetResult();
    }

    private async Task<string> StartStream(
        SonioxSocketSender sender,
        StreamConfig config,
        CancellationToken cancellationToken)
    {
        var streamId = $"{config.SessionId}-{++StreamCount}";
        _whenStreamTerminated[streamId] = TaskCompletionSourceExt.New();
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
        while (webSocket.State == WebSocketState.Open) {
            var receiveTask = ReceiveMessage(webSocket, buffer, cancellationToken);
            var completedTask = await Task.WhenAny(receiveTask, _whenDone.Task).ConfigureAwait(false);
            if (completedTask == _whenDone.Task && !receiveTask.IsCompleted) {
                _ = receiveTask.SilentAwait(false);
                return;
            }

            var message = await receiveTask.ConfigureAwait(false);
            if (message == null)
                return;

            var response = JsonSerializer.Deserialize<SonioxTtsResponse>(message, JsonOptions);
            if (response == null)
                continue;
            if (response.ErrorCode is { } errorCode)
                throw StandardError.External(
                    $"Soniox TTS error {errorCode} for #{sessionId}: {response.ErrorMessage}");

            var streamId = response.StreamId ?? "";
            if (!response.Audio.IsNullOrEmpty())
                await pcm.WriteAsync(Convert.FromBase64String(response.Audio), cancellationToken).ConfigureAwait(false);
            if (response.Terminated && _whenStreamTerminated.TryGetValue(streamId, out var whenTerminated))
                whenTerminated.TrySetResult();
        }
    }

    private static async Task<string?> ReceiveMessage(
        ClientWebSocket webSocket,
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

    // Nested types

    private sealed record StreamConfig(string SessionId, string ApiKey, string Language, string Voice);
}
