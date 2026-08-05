using System.Numerics;
using ActualChat.Audio;
using Microsoft.IO;
using OpenAI.Audio;
using OpenAI;

namespace ActualChat.Transcription;

public sealed class OpenAITranscriber : IOfflineTranscriber
{
    // We always run OpenAI's current best transcription model; Options.Model exists
    // only so the A/B diagnostic test can compare candidates.
    public const string DefaultModel = "gpt-transcribe";

    public class Options
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = DefaultModel;
    }

    private readonly Options _options;
    private readonly AudioClient _audioClient;

    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    private ILogger Log { get; }
    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.OpenAIOffline,
        Kind = TranscriberKind.Offline,
        // gpt-transcribe takes a free-form prompt, and the duration is known here, so the budget
        // scales with it: ~2x what the audio itself costs, floored so short phrases still get some
        // context and capped at what 30s of audio earns, since past that it stops paying off.
        ContextPolicy = new TranscriptionContextPolicy {
            MinChars = 80,
            MaxChars = 600,
            CharsPerAudioSecond = 20,
        },
    };

    public OpenAITranscriber(Options options, IServiceProvider services)
    {
        _options = options;
        Log = services.LogFor(GetType());
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
        var client = new OpenAIClient(options.ApiKey);
        _audioClient = client.GetAudioClient(options.Model);
    }

    public async Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        try {
            var stream = await PrepareOggStream(audioSource, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false)) {
                var options1 = new AudioTranscriptionOptions {
                    Language = GetSupportedLanguage(options),
                };
                var prompt = BuildPrompt(options, audioSource);
                if (!prompt.IsNullOrEmpty())
                    options1.Prompt = prompt;
                // Word timestamps come back only in the verbose response format, which the GPT
                // audio models don't support - asking them for one just costs quality.
                if (_options.Model.StartsWith("whisper")) {
                    options1.ResponseFormat = AudioTranscriptionFormat.Verbose;
                    options1.TimestampGranularities = AudioTimestampGranularities.Word;
                }
                // The .ogg extension is what tells the API the audio format.
                const string filename = "speech.ogg";
                AudioTranscription transcription = await _audioClient
                    .TranscribeAudioAsync(stream, filename, options1, cancellationToken)
                    .ConfigureAwait(false);
                return new Transcript(transcription.Text, BuildTimeMap(transcription), []);
            }
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to transcribe audio");
            return null;
        }

        static string GetSupportedLanguage(TranscriptionOptions options)
            => options.Language.IsoCode;
    }

    // Private methods

    private string BuildPrompt(TranscriptionOptions options, AudioSource audioSource)
    {
        if (Info.ContextPolicy is not { } policy || options.Context.IsNone)
            return "";

        var duration = audioSource.WhenDurationAvailable.IsCompletedSuccessfully
            ? audioSource.Duration
            : (TimeSpan?)null;
        var text = options.Context.Render(policy.GetMaxChars(duration));
        // OpenAI takes one free-form prompt, so the topic and the preceding speech are concatenated.
        return text.Summary.IsNullOrEmpty()
            ? text.Text
            : $"{text.Summary}\n{text.Text}".Trim();
    }

    private async Task<RecyclableMemoryStream> PrepareOggStream(
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var bufferSize = (int)Constants.Audio.MaxStreamDuration.TotalSeconds * Constants.Audio.Bitrate / 8;
        var stream = MemoryStreamManager.Default.GetStream(nameof(OpenAITranscriber), bufferSize);
        var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(audioSource, cancellationToken);
        var audioStream = byteFrameStream.Select(f => f.Buffer);
        await foreach (var chunk in audioStream.ConfigureAwait(false))
            stream.Write(chunk);
        stream.Position = 0;
        return stream;
    }

    private static LinearMap BuildTimeMap(AudioTranscription transcription)
    {
        var linearMap = LinearMap.Zero;
        var text = transcription.Text;
        if (transcription.Words.Count <= 0)
            return linearMap;

        var start = 0;
        foreach (var transcriptionWord in transcription.Words) {
            var i = text.IndexOf(transcriptionWord.Word, start);
            if (i < 0)
                continue;

            linearMap = linearMap.Append(new Vector2(i, (float)transcriptionWord.StartTime.TotalSeconds));
            start = i + transcriptionWord.Word.Length;
            linearMap = linearMap.Append(new Vector2(start, (float)transcriptionWord.EndTime.TotalSeconds));
        }

        // A degenerate map makes the caller DTW-remap the realtime one instead of trusting this.
        return linearMap.IsValid() ? linearMap : LinearMap.Zero;
    }
}
