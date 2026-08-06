using System.Net.WebSockets;
using System.Text;
using ActualChat.Audio;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.ElevenLabs;

namespace ActualChat.Transcription;

/// <summary>
/// Real-time transcriber built on ElevenLabs Scribe v2 Realtime (WebSocket).
/// </summary>
public sealed class ElevenLabsTranscriber : ITranscriber
{
    private const string BaseUrl = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";
    private const string Model = "scribe_v2_realtime";
    private const int SampleRate = Constants.Audio.RecordingSampleRate;
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private IServiceProvider Services { get; }
    private CoreServerSettings CoreServerSettings { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.ElevenLabsStream,
        Kind = TranscriberKind.Stream,
        IsLanguageDetectionSupported = true,
        // previous_text on the first chunk is a prose prefix, unlike the batch endpoint's
        // keyterm list - streaming can't know the duration, so it's a flat allowance.
        ContextPolicy = new TranscriptionContextPolicy { MaxChars = 200 },
    };

    public ElevenLabsTranscriber(IServiceProvider services)
    {
        Services = services;
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
        var apiKey = CoreServerSettings.ElevenLabsKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:ElevenLabsKey is not set.");

        Exception? error = null;
        using var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("xi-api-key", apiKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try {
            await webSocket.ConnectAsync(GetUrl(options), cancellationToken).ConfigureAwait(false);
            await TranscriberHelper.WhenPushAndRead(
                    PushAudio(webSocket, audioSource, options, cts.Token),
                    ReadTranscripts(webSocket, output, audioStreamId, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "ElevenLabs transcription failed for #{StreamId}", audioStreamId);
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
            $"model_id={Model}",
            $"audio_format=pcm_{SampleRate}",
            "commit_strategy=vad",
            $"vad_threshold={VadThreshold}",
            $"vad_silence_threshold_secs={VadSilenceThresholdSeconds}",
            "include_timestamps=true",
            $"include_language_detection={options.DetectLanguage.ToString().ToLower()}",
        };
        if (!options.DetectLanguage)
            query.Add($"language_code={options.Language.ToElevenLabs()}");
        return new Uri($"{BaseUrl}?{query.ToDelimitedString("&")}");
    }

    private async Task PushAudio(
        ClientWebSocket webSocket,
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var silenceAudio = await TranscriberHelper.GetSilenceAudioSource(Services).ConfigureAwait(false);
        var transcribedAudioSource = TranscriberHelper.AddSilentPrefixAndSuffix(
            audioSource,
            silenceAudio,
            SilentPrefixDuration,
            SilentSuffixDuration,
            cancellationToken);

        using var decoder = new OpusToPcmDecoder();
        var clock = Clocks.CpuClock;
        var startedAt = clock.Now;
        var nextChunkAt = startedAt;
        var previousText = BuildPreviousText(options);
        await foreach (var frame in transcribedAudioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
            var delay = nextChunkAt - clock.Now;
            if (delay > TimeSpan.Zero)
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

            var pcm = decoder.Decode(frame.Data.Span);
            if (pcm.Length != 0) {
                var message = new ElevenLabsAudioChunk {
                    AudioBase64 = Convert.ToBase64String(pcm),
                    SampleRate = SampleRate,
                    PreviousText = previousText.NullIfEmpty(),
                };
                previousText = "";
                await Send(webSocket, message, cancellationToken).ConfigureAwait(false);
            }

            var processedAudioDuration = (frame.Offset + frame.Duration - SilentPrefixDuration).Positive();
            if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully)
                processedAudioDuration = TimeSpanExt.Min(audioSource.Duration, processedAudioDuration);
            nextChunkAt = startedAt
                + TimeSpan.FromSeconds(processedAudioDuration.TotalSeconds / Speed)
                - TimeSpan.FromMilliseconds(50);
        }

        // There is no end-of-audio message, so an explicit commit is the only way to have the
        // trailing segment finalized before the socket closes.
        await Send(webSocket, new ElevenLabsAudioChunk { AudioBase64 = "", IsCommitted = true }, cancellationToken)
            .ConfigureAwait(false);
    }

    private string BuildPreviousText(TranscriptionOptions options)
    {
        if (Info.ContextPolicy is not { } policy || options.Context.IsNone)
            return "";

        var text = options.Context.Render(policy.GetMaxChars(null));
        return text.Summary.IsNullOrEmpty()
            ? text.Text
            : $"{text.Summary}\n{text.Text}".Trim();
    }

    private static Task Send(
        ClientWebSocket webSocket,
        ElevenLabsAudioChunk message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return webSocket
            .SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReadTranscripts(
        ClientWebSocket webSocket,
        ChannelWriter<Transcript> output,
        string audioStreamId,
        CancellationToken cancellationToken)
    {
        var builder = new ElevenLabsTranscriptBuilder();
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

            var response = JsonSerializer.Deserialize<ElevenLabsMessage>(message.ToString(), JsonOptions);
            if (response == null)
                continue;

            hasMessages = true;
            switch (response.MessageType) {
            case "session_started":
                Log.LogDebug("ElevenLabs session {SessionId} for #{StreamId}", response.SessionId, audioStreamId);
                break;
            case "partial_transcript":
                await output.WriteAsync(builder.Update(response.Text ?? ""), cancellationToken).ConfigureAwait(false);
                break;
            case "committed_transcript_with_timestamps":
                // Every segment is also announced as a plain committed_transcript with the same
                // text; committing both would append it twice. include_timestamps is always on,
                // so the timestamped one is guaranteed to arrive and is the richer of the two.
                if (!response.Text.IsNullOrEmpty())
                    await output.WriteAsync(builder.Commit(response), cancellationToken).ConfigureAwait(false);
                break;
            case "final_transcript":
            case "committed_transcript":
                break;
            case "error":
            case "auth_error":
                throw StandardError.External(
                    $"ElevenLabs error for #{audioStreamId}: {response.Error ?? response.Text}");
            default:
                Log.LogWarning("ElevenLabs sent {MessageType} for #{StreamId}: {Message}",
                    response.MessageType, audioStreamId, message.ToString());
                break;
            }
        }

        // A socket that closed before saying anything means the session never started - most
        // often a rejected key. Reporting that as an empty transcript would hide it.
        if (!hasMessages)
            throw StandardError.External($"ElevenLabs sent nothing for #{audioStreamId}: "
                + $"{webSocket.CloseStatus} {webSocket.CloseStatusDescription}");

        await output.WriteAsync(builder.Complete(), cancellationToken).ConfigureAwait(false);
    }

    // Nested types

    private sealed class ElevenLabsAudioChunk
    {
        [JsonPropertyName("message_type")] public string MessageType => "input_audio_chunk";
        [JsonPropertyName("audio_base_64")] public string AudioBase64 { get; init; } = "";
        [JsonPropertyName("commit")] public bool IsCommitted { get; init; }
        [JsonPropertyName("sample_rate")] public int? SampleRate { get; init; }
        [JsonPropertyName("previous_text")] public string? PreviousText { get; init; }
    }
}
