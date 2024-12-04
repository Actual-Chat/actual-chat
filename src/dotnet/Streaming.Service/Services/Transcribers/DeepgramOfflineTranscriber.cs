using System.Numerics;
using ActualChat.Audio;
using ActualChat.Streaming.Module;
using ActualChat.Transcription;
using Deepgram;
using Deepgram.Constants;
using Deepgram.Models.Authenticate.v1;
using Deepgram.Models.Listen.v1.REST;

namespace ActualChat.Streaming.Services.Transcribers;

public class DeepgramOfflineTranscriber  : ITranscriber
{
    private ILogger Log { get; }
    private MomentClockSet Clocks { get; }
    private StreamingSettings StreamingSettings { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }

    public DeepgramOfflineTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        StreamingSettings = services.GetRequiredService<StreamingSettings>();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
    }

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        var transcriptState = new DeepgramTranscribeState(audioSource, output);
        Exception? error = null;
        try {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var apiKey = StreamingSettings.DeepgramKey;
            var deepgramClient = ClientFactory.CreateListenRESTClient(apiKey, new DeepgramHttpClientOptions(apiKey));
            using var transcribeTcs = cancellationToken.CreateLinkedTokenSource();
            var bufferSize = (int)Constants.Audio.MaxStreamDuration.TotalSeconds * Constants.Audio.Bitrate / 8;
            Stream stream = MemoryStreamManager.Default.GetStream(nameof(DeepgramOfflineTranscriber), bufferSize);
            var language = GetSupportedLanguage(options);
            var schema = new PreRecordedSchema {
                Language = language,
                Punctuate = true,
                Diarize = false,
                Encoding = AudioEncoding.OggOpus,
                Model = "whisper-large",
            };
            var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(audioSource, cancellationToken);
            var audioStream = byteFrameStream.Select(f => f.Buffer);
            await foreach (var chunk in audioStream)
                stream.Write(chunk);
            stream.Position = 0;

            var response = await deepgramClient.TranscribeFile(stream, schema, transcribeTcs).ConfigureAwait(false);
            ProcessResponse(transcriptState, response);
        }
        catch (Exception e) {
            error = e;
            Log.LogError(e, "Error transcribing {StreamId}", audioStreamId);
            throw;
        }
        finally {
            output.TryComplete(error);
        }
        return;

        string GetSupportedLanguage(TranscriptionOptions options1)
        {
            return options1.Language.Id.Value switch {
                "en-US" => "en-US",
                "en-GB" => "en-GB",
                "en-IN" => "en-IN",
                "fr-FR" => "fr",
                "fr-CA" => "fr-CA",
                "de-DE" => "de",
                "hi-IN" => "hi",
                "pt-BR" => "pt-BR",
                "pt-PT" => "pt",
                "es-ES" => "es",
                "es-MX" => "es-419",
                "es-US" => "es-419",
                "ru-RU" => "ru",
                "zh-CN" => "zh-CN",
                _ => throw StandardError.NotSupported(typeof(DeepgramTranscriber), $"Language '{options1.Language.Id}' is not supported"),
            };
        }
    }

    private void ProcessResponse(DeepgramTranscribeState state, SyncResponse response)
    {
        var result = response.Results;
        if (result == null || result.Channels == null || result.Channels.Count == 0 || result.Channels[0].Alternatives == null) {
            Log.LogWarning("Received empty response from Deepgram");
            return;
        }

        var alternative = result.Channels[0].Alternatives?.FirstOrDefault();
        var text = alternative?.Transcript ?? "";
        Log.LogDebug("Transcript: {Transcript}", text);

        var mapPoints = new List<Vector2>();
        var parsedOffset = 0;
        foreach (var word in alternative?.Words ?? []) {
            var wordStartTime = (float?)word.Start ?? 0;
            if (word.PunctuatedWord == null)
                continue;

            var wordStart = text.OrdinalIgnoreCaseIndexOf(word.PunctuatedWord, parsedOffset);
            if (wordStart < 0)
                continue;

            var wordEndTime = (float?)word.End ?? 0;
            var wordEnd = wordStart + word.PunctuatedWord.Length;

            mapPoints.Add(new Vector2(wordStart, wordStartTime));
            mapPoints.Add(new Vector2(wordEnd, wordEndTime));

            parsedOffset = wordStart + word.PunctuatedWord.Length;
        }

        var timeMap = new LinearMap(mapPoints.ToArray()).Simplify(Transcript.TimeMapEpsilon);
        state.Append(text, timeMap);
        state.MakeStable();
        state.Output.WriteAsync(state.Unstable);
    }
}
