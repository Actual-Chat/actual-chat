using System.Net.WebSockets;
using System.Text;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Soniox;

namespace ActualChat.Transcription;

/// <summary>
/// One connection and one stream per text chunk of Soniox's <c>tts-rt</c> WebSocket API: a text
/// chunk in, 48 kHz PCM out. Soniox holds synthesis until it sees <c>text_end</c> and times out an
/// idle connection, so nothing is kept open between chunks - each chunk gets a fresh connection,
/// opened and closed around just that chunk.
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

    private CoreServerSettings CoreServerSettings { get; } = services.GetRequiredService<CoreServerSettings>();
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
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                if (chunk.IsNullOrWhiteSpace())
                    continue;

                await SpeakChunk(sessionId, apiKey, language, voice, chunk, pcm, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox TTS failed for #{SessionId}", sessionId);
            throw;
        }
        finally {
            pcm.TryComplete(error);
        }
    }

    // Private methods

    private async Task SpeakChunk(
        string sessionId,
        string apiKey,
        string language,
        string voice,
        string chunk,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        StreamCount++;
        var streamId = $"{sessionId}-{StreamCount}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TtsChunkTimeout);
        using var webSocket = new ClientWebSocket();
        try {
            await webSocket.ConnectAsync(new Uri(Url), cts.Token).ConfigureAwait(false);
            await Send(webSocket, new {
                api_key = apiKey,
                model = Model,
                language,
                voice,
                audio_format = PcmFormat,
                sample_rate = SampleRate,
                stream_id = streamId,
            }, cts.Token).ConfigureAwait(false);
            await Send(webSocket, new { text = chunk, text_end = true, stream_id = streamId }, cts.Token)
                .ConfigureAwait(false);
            await ReadUntilTerminated(webSocket, streamId, pcm, cts.Token).ConfigureAwait(false);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token).SilentAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw StandardError.External($"Soniox TTS chunk #{streamId} did not finish within {TtsChunkTimeout}.");
        }
    }

    private static async Task ReadUntilTerminated(
        ClientWebSocket webSocket,
        string streamId,
        ChannelWriter<byte[]> pcm,
        CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        while (true) {
            var message = await ReceiveMessage(webSocket, buffer, cancellationToken).ConfigureAwait(false);
            if (message == null)
                throw StandardError.External($"Soniox TTS closed the connection before finishing #{streamId}.");

            var response = JsonSerializer.Deserialize<SonioxTtsResponse>(message, JsonOptions);
            if (response == null)
                continue;
            if (response.ErrorCode is { } errorCode)
                throw StandardError.External(
                    $"Soniox TTS error {errorCode} ({response.ErrorType}) for #{streamId}: {response.ErrorMessage}");

            if (!response.Audio.IsNullOrEmpty())
                await pcm.WriteAsync(Convert.FromBase64String(response.Audio), cancellationToken).ConfigureAwait(false);
            if (response.Terminated)
                return;
        }
    }

    private static Task Send(ClientWebSocket webSocket, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return webSocket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);
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
}
