using System.Net.WebSockets;
using System.Text;
using ActualChat.Audio;
using ActualChat.Module;
using static ActualChat.Constants.Transcription.Soniox;

namespace ActualChat.Transcription;

/// <summary>
/// Real-time transcriber built on Soniox's <c>stt-rt</c> WebSocket API.
/// </summary>
public sealed class SonioxTranscriber : ITranscriber
{
    private const string Url = "wss://stt-rt.soniox.com/transcribe-websocket";
    private const string Model = "stt-rt-v5";
    private const string EndpointToken = "<end>";
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly byte[] KeepAlivePayload = """{"type":"keepalive"}"""u8.ToArray();

    private IServiceProvider Services { get; }
    private CoreServerSettings CoreServerSettings { get; }
    private MomentClockSet Clocks { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    private ILogger Log { get; }

    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.SonioxStream,
        // Soniox re-decides earlier tokens as more audio arrives, so its live output already
        // carries the correction an offline pass would otherwise provide.
        Kind = TranscriberKind.Stream,
        Languages = SonioxLanguage.Supported,
        DetectLanguages = SonioxLanguage.Supported,
        IsLanguageDetectionSupported = true,
        // Streaming can't know the duration up front, so it gets the flat allowance only.
        ContextPolicy = new TranscriptionContextPolicy { MaxChars = 60 },
    };

    public SonioxTranscriber(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = Constants.Transcription.StreamPageDuration,
        });
    }

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        Exception? error = null;
        using var webSocket = new ClientWebSocket();
        using var sender = new Sender(webSocket, Clocks.CpuClock);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? keepAliveTask = null;
        try {
            await webSocket.ConnectAsync(new Uri(Url), cancellationToken).ConfigureAwait(false);
            await SendConfig(sender, apiKey, options, cancellationToken).ConfigureAwait(false);

            keepAliveTask = KeepAlive(sender, cts.Token);
            await TranscriberHelper.WhenPushAndRead(
                    PushAudio(sender, audioSource, cts.Token),
                    ReadTranscripts(webSocket, output, audioStreamId, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            if (e is not OperationCanceledException)
                Log.LogError(e, "Soniox transcription failed for #{StreamId}", audioStreamId);
            throw;
        }
        finally {
            await cts.CancelAsync().ConfigureAwait(false);
            if (keepAliveTask != null)
                await keepAliveTask.SilentAwait(false);
            output.TryComplete(error);
        }
    }

    // Private methods

    private async Task SendConfig(
        Sender sender,
        string apiKey,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var policy = Info.ContextPolicy;
        var config = new Dictionary<string, object?> {
            ["api_key"] = apiKey,
            ["model"] = Model,
            ["audio_format"] = "auto",
            ["enable_language_identification"] = options.DetectLanguage,
            // Without it nothing is finalized until the stream ends, so Complete() would
            // drop the entire transcript as an unfinalized tail.
            ["enable_endpoint_detection"] = true,
            ["max_endpoint_delay_ms"] = MaxEndpointDelayMs,
            ["endpoint_sensitivity"] = EndpointSensitivity,
        };
        // Dictionary values are serialized even when null, and Soniox rejects a null context.
        if (SonioxContext.Build(options.Context, policy) is { } context)
            config["context"] = context;
        // Hints come from what the chat actually selected; with nothing selected we send none
        // rather than an empty array. They're never strict, so they only nudge the model.
        if (options.GetLanguageHints(SonioxLanguage.ToSoniox) is { Length: > 0 } languageHints)
            config["language_hints"] = languageHints;
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await sender
            .Send(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task KeepAlive(Sender sender, CancellationToken cancellationToken)
    {
        var clock = Clocks.CpuClock;
        while (true) {
            var delay = KeepAlivePeriod - (clock.Now - sender.LastSendAt);
            if (delay > TimeSpan.Zero) {
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await sender.Send(KeepAlivePayload, WebSocketMessageType.Text, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PushAudio(
        Sender sender,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var silenceAudio = await TranscriberHelper.GetSilenceAudioSource(Services).ConfigureAwait(false);
        var transcribedAudioSource = TranscriberHelper.AddSilentPrefixAndSuffix(
            audioSource,
            silenceAudio,
            SilentPrefixDuration,
            SilentSuffixDuration,
            cancellationToken);

        var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(transcribedAudioSource, cancellationToken);
        var clock = Clocks.CpuClock;
        var startedAt = clock.Now;
        var nextChunkAt = startedAt;
        await foreach (var (chunk, lastFrame) in byteFrameStream.ConfigureAwait(false)) {
            var delay = nextChunkAt - clock.Now;
            if (delay > TimeSpan.Zero)
                await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

            await sender.Send(chunk, WebSocketMessageType.Binary, cancellationToken).ConfigureAwait(false);
            if (lastFrame == null)
                continue;

            var processedAudioDuration = (lastFrame.Offset + lastFrame.Duration - SilentPrefixDuration).Positive();
            if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully)
                processedAudioDuration = TimeSpanExt.Min(audioSource.Duration, processedAudioDuration);
            nextChunkAt = startedAt
                + TimeSpan.FromSeconds(processedAudioDuration.TotalSeconds / Speed)
                - TimeSpan.FromMilliseconds(50);
        }

        // An empty frame is Soniox's "no more audio" signal; without it the server keeps waiting
        // and drops the connection on its 20s inactivity timeout. It must be a Text frame:
        // an empty Binary one goes unnoticed, which is exactly how that timeout used to fire.
        await sender
            .Send(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Text, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReadTranscripts(
        ClientWebSocket webSocket,
        ChannelWriter<Transcript> output,
        string audioStreamId,
        CancellationToken cancellationToken)
    {
        var builder = new SonioxTranscriptBuilder();
        var buffer = new ArraySegment<byte>(new byte[16 * 1024]);
        var message = new StringBuilder();
        var hasFinished = false;
        while (!hasFinished && webSocket.State == WebSocketState.Open) {
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

            var response = JsonSerializer.Deserialize<SonioxResponse>(message.ToString(), JsonOptions);
            if (response == null)
                continue;
            if (response.ErrorCode is { } errorCode)
                throw StandardError.External(
                    $"Soniox error {errorCode} for #{audioStreamId}: {response.ErrorMessage}");

            if (response.Tokens?.Any(x => x.Text == EndpointToken) == true)
                Log.LogInformation("Soniox endpoint for #{StreamId} at {AudioMs}ms of audio",
                    audioStreamId, response.TotalAudioProcMs);
            if (response.Tokens is { Length: > 0 } tokens)
                await output.WriteAsync(builder.Update(tokens), cancellationToken).ConfigureAwait(false);

            if (response.Finished) {
                await output.WriteAsync(builder.Complete(), cancellationToken).ConfigureAwait(false);
                hasFinished = true;
            }
        }

        if (hasFinished)
            return;

        // The socket ended without a finished response, so nothing more will be finalized -
        // emit what we have, tail included, rather than leaving only unstable updates behind.
        var partial = builder.Complete(false);
        if (!partial.Text.IsNullOrEmpty())
            await output.WriteAsync(partial, cancellationToken).ConfigureAwait(false);
    }

    // Nested types

    // ClientWebSocket allows just one send at a time, and the keepalive loop sends
    // concurrently with the audio push.
    private sealed class Sender(ClientWebSocket webSocket, MomentClock clock) : IDisposable
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        public Moment LastSendAt { get; private set; } = clock.Now;

        public void Dispose()
            => _lock.Dispose();

        public async Task Send(
            ReadOnlyMemory<byte> data,
            WebSocketMessageType messageType,
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await webSocket.SendAsync(data, messageType, true, cancellationToken).ConfigureAwait(false);
                LastSendAt = clock.Now;
            }
            finally {
                _lock.Release();
            }
        }
    }
}
