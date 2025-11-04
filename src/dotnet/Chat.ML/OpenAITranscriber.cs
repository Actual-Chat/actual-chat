using System.Numerics;
using ActualChat.Audio;
using ActualChat.Transcription;
using Microsoft.IO;
using OpenAI.Audio;
using OpenAI;

namespace ActualChat.Chat.ML;

public class OpenAITranscriber
{
    private readonly AudioClient _audioClient;

    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    private ILogger Log { get; }

    public OpenAITranscriber(IServiceProvider services, string apiKey)
    {
        Log = services.LogFor(GetType());
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
        var client = new OpenAIClient(apiKey);
        //var model = "whisper-1";
        //var model = "gpt-4o-mini-transcribe";
        var model = "gpt-4o-transcribe";
        _audioClient = client.GetAudioClient(model);// The model to use for the transcription
    }

    public async Task<Transcript?> Transcribe(AudioSource audioSource, CancellationToken cancellationToken)
    {
        try {
            var stream = await PrepareOggStream(audioSource, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false)) {
                var options = new AudioTranscriptionOptions() {
                    Language = "ru",
                    //ResponseFormat = AudioTranscriptionFormat.Verbose,
                    TimestampGranularities = AudioTimestampGranularities.Word,
                };
                const string filename = "speech.ogg"; // use file name with ogg extension to indicate the audio format.
                AudioTranscription transcription = await _audioClient
                    .TranscribeAudioAsync(stream, filename, options, cancellationToken)
                    .ConfigureAwait(false);
                return new Transcript(transcription.Text, BuildTimeMap(transcription), []);
            }
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to transcribe audio");
            return null;
        }
    }

    private async Task<RecyclableMemoryStream> PrepareOggStream(AudioSource audioSource, CancellationToken cancellationToken)
    {
        var bufferSize = (int)Constants.Audio.MaxStreamDuration.TotalSeconds * Constants.Audio.Bitrate / 8;
        var stream = MemoryStreamManager.Default.GetStream(nameof(OpenAITranscriber), bufferSize);
        //var language = GetSupportedLanguage(options);
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
