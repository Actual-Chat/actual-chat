using System.Net.WebSockets;
using System.Text;
using ActualChat.Audio;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Xai;

namespace ActualChat.Transcription;

/// <summary>
/// Real-time transcriber built on xAI Grok Voice Transcribe 2.0 (WebSocket).
/// </summary>
public sealed class XaiTranscriber : ITranscriber
{
    private const string BaseUrl = "wss://api.x.ai/v1/stt";
    private const string Model = "grok-voice-transcribe-2.0";
    private static readonly byte[] AudioDoneMessage = """{"type":"audio.done"}"""u8.ToArray();

    private CoreServerSettings CoreServerSettings { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.XaiStream,
        Kind = TranscriberKind.Stream,
        Languages = XaiLanguage.Supported,
        DetectLanguages = XaiLanguage.Supported,
        IsLanguageDetectionSupported = true,
    };

    public XaiTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
    }

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        var apiKey = CoreServerSettings.XaiApiKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:XaiApiKey is not set.");

        Exception? error = null;
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try {
            await webSocket.ConnectAsync(GetUrl(options), cancellationToken).ConfigureAwait(false);
            await TaskExt.WhenPushAndRead(
                    PushAudio(webSocket, audioSource, cts.Token),
                    ReadTranscripts(webSocket, output, audioStreamId, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "xAI transcription failed for #{StreamId}", audioStreamId);
            throw;
        }
        finally {
            await cts.CancelAsync().ConfigureAwait(false);
            output.TryComplete(error);
        }
    }

    // Private methods

    private static Uri GetUrl(TranscriptionOptions options)
    {
        var query = new List<string> {
            $"model={Model}",
            "encoding=opus",
            "interim_results=true",
            $"endpointing={(int)Endpointing.TotalMilliseconds}",
        };
        if (!options.DetectLanguage)
            query.Add($"language={options.Language.ToXai()}");
        return new Uri($"{BaseUrl}?{query.ToDelimitedString("&")}");
    }

    private async Task PushAudio(
        ClientWebSocket webSocket,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        // The pipeline carries raw Opus packets, and xAI takes exactly one per binary frame.
        var clock = Clocks.CpuClock;
        var startedAt = clock.Now;
        var nextChunkAt = startedAt;
        await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
            var delay = nextChunkAt - clock.Now;
            if (delay > TimeSpan.Zero)
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

            await webSocket
                .SendAsync(frame.Data, WebSocketMessageType.Binary, true, cancellationToken)
                .ConfigureAwait(false);

            var processedAudioDuration = frame.Offset + frame.Duration;
            nextChunkAt = startedAt
                + TimeSpan.FromSeconds(processedAudioDuration.TotalSeconds / Speed)
                - TimeSpan.FromMilliseconds(50);
        }

        await webSocket
            .SendAsync(AudioDoneMessage, WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReadTranscripts(
        ClientWebSocket webSocket,
        ChannelWriter<Transcript> output,
        string audioStreamId,
        CancellationToken cancellationToken)
    {
        var builder = new SegmentTranscriptBuilder();
        var buffer = new ArraySegment<byte>(new byte[16 * 1024]);
        var message = new StringBuilder();
        var hasMessages = false;
        while (webSocket.State == WebSocketState.Open) {
            message.Clear();
            WebSocketReceiveResult result;
            var isClosed = false;
            do {
                result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) {
                    isClosed = true;
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer.Array!, 0, result.Count));
            } while (!result.EndOfMessage);

            if (isClosed)
                break;

            var response = JsonSerializer.Deserialize<XaiMessage>(message.ToString());
            if (response == null)
                continue;

            hasMessages = true;
            switch (response.Type) {
            case "transcript.created":
                break;
            case "transcript.partial" when response.IsFinal:
                if (!response.Text.IsNullOrEmpty()) {
                    var words = (response.Words ?? []).Select(x => (x.Text, x.Start, x.End));
                    var transcript = response.IsSpeechFinal
                        ? builder.Recommit(response.Start, response.Text, words, response.Language)
                        : builder.Commit(response.Text, words, response.Language, response.Start);
                    await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
                }
                break;
            case "transcript.partial":
                await output.WriteAsync(builder.Update(response.Text ?? ""), cancellationToken).ConfigureAwait(false);
                break;
            case "transcript.done":
                // Its text is empty in practice: everything arrives in the final partials.
                await output.WriteAsync(builder.Complete(response.Language), cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "error":
                throw StandardError.External($"xAI error for #{audioStreamId}: {response.Message}");
            default:
                Log.LogWarning("xAI sent {Type} for #{StreamId}: {Message}",
                    response.Type, audioStreamId, message.ToString());
                break;
            }
        }

        // A socket that closed before saying anything means the session never started - most
        // often a rejected key. Reporting that as an empty transcript would hide it.
        if (!hasMessages)
            throw StandardError.External($"xAI sent nothing for #{audioStreamId}: "
                + $"{webSocket.CloseStatus} {webSocket.CloseStatusDescription}");

        await output.WriteAsync(builder.Complete(), cancellationToken).ConfigureAwait(false);
    }

    // Nested types

    private sealed class XaiMessage
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("words")] public XaiWord[]? Words { get; set; }
        [JsonPropertyName("is_final")] public bool IsFinal { get; set; }
        [JsonPropertyName("speech_final")] public bool IsSpeechFinal { get; set; }
        [JsonPropertyName("start")] public double Start { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    private sealed class XaiWord
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("start")] public double Start { get; set; }
        [JsonPropertyName("end")] public double End { get; set; }
    }
}
