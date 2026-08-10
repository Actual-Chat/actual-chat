using ActualChat.Audio;
using Microsoft.IO;

namespace ActualChat.Transcription;

/// <summary>
/// One-shot transcriber built on Soniox's async REST API:
/// upload the audio, create a transcription, poll, then fetch the transcript.
/// </summary>
public sealed class SonioxOfflineTranscriber : IOfflineTranscriber
{
    private const string Model = "stt-async-v5";
    private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(1);

    private SonioxClient Client { get; }
    private SonioxCleaner Cleaner { get; }
    private MomentClockSet Clocks { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    private ILogger Log { get; }
    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.SonioxOffline,
        Kind = TranscriberKind.Offline,
        Languages = SonioxLanguage.Supported,
        DetectLanguages = SonioxLanguage.Supported,
        IsLanguageDetectionSupported = true,
        // The duration is known here, so the budget scales with it: ~2x what the audio itself costs,
        // floored so short phrases still get some context and capped at what 30s of audio earns.
        ContextPolicy = new TranscriptionContextPolicy {
            MinChars = 80,
            MaxChars = 600,
            CharsPerAudioSecond = 20,
        },
    };

    public SonioxOfflineTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        Client = services.GetRequiredService<SonioxClient>();
        Cleaner = services.GetRequiredService<SonioxCleaner>();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
    }

    public async Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        string? fileId = null;
        string? transcriptionId = null;
        try {
            fileId = await UploadAudio(audioSource, cancellationToken).ConfigureAwait(false);
            transcriptionId = await CreateTranscription(fileId, options, audioSource, cancellationToken)
                .ConfigureAwait(false);
            await WaitForCompletion(transcriptionId, cancellationToken).ConfigureAwait(false);
            return await GetTranscript(transcriptionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Soniox offline transcription failed");
            return null;
        }
        finally {
            // Soniox caps the stored file count per organization, so uploads must be dropped even
            // when transcription fails. The cleaner keeps the two deletes off the latency path.
            Cleaner.Enqueue(transcriptionId, fileId);
        }
    }

    // Private methods

    private async Task<string> UploadAudio(AudioSource audioSource, CancellationToken cancellationToken)
    {
        var stream = await ToOggStream(audioSource, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            return await Client.UploadFile(stream, "speech.ogg", cancellationToken).ConfigureAwait(false);
    }

    private Task<string> CreateTranscription(
        string fileId,
        TranscriptionOptions options,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?> {
            ["file_id"] = fileId,
            ["model"] = Model,
            ["enable_language_identification"] = options.DetectLanguage,
        };
        // Dictionary values are serialized even when null, and Soniox rejects a null context.
        var duration = audioSource.WhenDurationAvailable.IsCompletedSuccessfully
            ? audioSource.Duration
            : (TimeSpan?)null;
        if (SonioxContext.Build(options.Context, Info.ContextPolicy, duration) is { } context)
            request["context"] = context;
        if (options.GetLanguageHints(SonioxLanguage.ToSoniox) is { Length: > 0 } languageHints)
            request["language_hints"] = languageHints;

        return Client.CreateTranscription(request, cancellationToken);
    }

    private async Task WaitForCompletion(string transcriptionId, CancellationToken cancellationToken)
    {
        while (true) {
            var status = await Client.GetTranscriptionStatus(transcriptionId, cancellationToken).ConfigureAwait(false);
            if (status.Status == "completed")
                return;
            if (status.Status == "error")
                throw StandardError.External($"Soniox transcription failed: {status.ErrorMessage}");

            await Clocks.CpuClock.Delay(PollPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Transcript?> GetTranscript(string transcriptionId, CancellationToken cancellationToken)
    {
        var response = await Client.GetTranscript(transcriptionId, cancellationToken).ConfigureAwait(false);
        if (response?.Tokens is not { Length: > 0 } tokens)
            return null;

        // The async API returns the whole transcript at once, and its tokens carry no is_final flag.
        var builder = new SonioxTranscriptBuilder();
        foreach (var token in tokens)
            token.IsFinal = true;
        builder.Update(tokens);
        return builder.Complete();
    }

    private async Task<RecyclableMemoryStream> ToOggStream(AudioSource audioSource, CancellationToken cancellationToken)
    {
        var bufferSize = (int)Constants.Audio.MaxStreamDuration.TotalSeconds * Constants.Audio.Bitrate / 8;
        var stream = MemoryStreamManager.Default.GetStream(nameof(SonioxOfflineTranscriber), bufferSize);
        var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(audioSource, cancellationToken);
        await foreach (var frame in byteFrameStream.ConfigureAwait(false))
            stream.Write(frame.Buffer);
        stream.Position = 0;
        return stream;
    }
}
