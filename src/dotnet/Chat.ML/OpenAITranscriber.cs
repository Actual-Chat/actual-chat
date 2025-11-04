using System.Numerics;
using ActualChat.Audio;
using ActualChat.Transcription;
using Microsoft.IO;
using OpenAI.Audio;
using OpenAI;

namespace ActualChat.Chat.ML;

public class OpenAITranscriber
{
    public class Options
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = ""; /* "gpt-4o-transcribe" or "gpt-4o-mini-transcribe" or "whisper-1"  */
    }

    private readonly Options _options;
    private readonly AudioClient _audioClient;

    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    private ILogger Log { get; }

    public OpenAITranscriber(Options options, IServiceProvider services)
    {
        _options = options;
        Log = services.LogFor(GetType());
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
        var client = new OpenAIClient(options.ApiKey);
        _audioClient = client.GetAudioClient(options.Model);// The model to use for the transcription
    }

    public async Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        try {
            var stream = await PrepareOggStream(audioSource, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false)) {
                var options1 = new AudioTranscriptionOptions() {
                    Language = GetSupportedLanguage(options),
                    TimestampGranularities = AudioTimestampGranularities.Word,
                };
                if (_options.Model.OrdinalStartsWith("whisper"))
                    options1.ResponseFormat = AudioTranscriptionFormat.Verbose;
                const string filename = "speech.ogg"; // use file name with ogg extension to indicate the audio format.
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
            => options.Language.Value.Substring(0, 2);
    }

    private async Task<RecyclableMemoryStream> PrepareOggStream(AudioSource audioSource, CancellationToken cancellationToken)
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

        linearMap.Append(new Vector2(0, 0));
        var start = 0;
        foreach (var transcriptionWord in transcription.Words) {
            var i = text.IndexOf(transcriptionWord.Word, start, StringComparison.Ordinal);
            if (i < 0)
                continue;
            linearMap.Append(new Vector2(i, (float)transcriptionWord.StartTime.TotalSeconds));
            start = i + transcriptionWord.Word.Length;
            linearMap.Append(new Vector2(start, (float)transcriptionWord.EndTime.TotalSeconds));
        }

        return linearMap;
    }
}
