using System.Net.Http.Headers;
using System.Numerics;
using ActualChat.Audio;
using ActualChat.Module;
using Microsoft.IO;

namespace ActualChat.Transcription;

/// <summary>
/// One-shot transcriber built on ElevenLabs Scribe v2: a single POST returns the whole
/// transcript, so unlike Soniox async there is no job to create and poll.
/// </summary>
public sealed class ElevenLabsOfflineTranscriber : IOfflineTranscriber
{
    private const string Url = "https://api.elevenlabs.io/v1/speech-to-text";
    private const string Model = "scribe_v2";
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private CoreServerSettings CoreServerSettings { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    private ILogger Log { get; }
    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.ElevenLabsOffline,
        Kind = TranscriberKind.Offline,
        IsLanguageDetectionSupported = true,
        // No ContextPolicy: the batch endpoint takes only a keyterm list, and our context is
        // prose. The realtime one accepts a prefix, so ElevenLabsTranscriber does declare one.
    };

    public ElevenLabsOfflineTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
    }

    public async Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        var apiKey = CoreServerSettings.ElevenLabsKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:ElevenLabsKey is not set.");

        try {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);

            var stream = await ToOggStream(audioSource, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false)) {
                using var content = new MultipartFormDataContent();
                using var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
                content.Add(fileContent, "file", "speech.ogg");
                content.Add(new StringContent(Model), "model_id");
                content.Add(new StringContent("word"), "timestamps_granularity");
                // Laughter and footsteps would land in the transcript as literal "(laughter)".
                content.Add(new StringContent("false"), "tag_audio_events");
                if (!options.DetectLanguage)
                    content.Add(new StringContent(options.Language.ToElevenLabs()), "language_code");

                using var response = await httpClient.PostAsync(Url, content, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw StandardError.External(
                        $"ElevenLabs transcription failed: {(int)response.StatusCode} {body}");
                }

                var result = await response.Content
                    .ReadFromJsonAsync<ElevenLabsResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return result == null ? null : ToTranscript(result);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "ElevenLabs offline transcription failed");
            return null;
        }
    }

    // Private methods

    private static Transcript ToTranscript(ElevenLabsResponse response)
    {
        var text = response.Text ?? "";
        if (response.Words is not { Length: > 0 } words)
            return new Transcript(text, LinearMap.Zero, []);

        var timeMap = LinearMap.Zero;
        var offset = 0;
        foreach (var word in words) {
            if (word.Type != "word" || word.Text.IsNullOrEmpty())
                continue;

            var start = text.IndexOf(word.Text, offset);
            if (start < 0)
                continue;

            timeMap = TryAppend(timeMap, start, (float)word.Start);
            offset = start + word.Text.Length;
            timeMap = TryAppend(timeMap, offset, (float)word.End);
        }

        // A degenerate map makes the caller DTW-remap the realtime one instead of trusting this.
        var languages = Language.TryParse(response.LanguageCode ?? "", out var language) ? new[] { language } : [];
        return new Transcript(text, timeMap.IsValid() ? timeMap : LinearMap.Zero, languages);
    }

    private static LinearMap TryAppend(LinearMap map, float x, float y)
    {
        // LinearMap requires strictly increasing points, and Zero already holds (0, 0), so the
        // first word's start would otherwise make the whole map invalid and get it thrown away.
        var points = map.Length;
        if (points > 0) {
            var last = map[points - 1];
            if (x <= last.X || y < last.Y)
                return map;
        }

        return map.Append(new Vector2(x, y));
    }

    private async Task<RecyclableMemoryStream> ToOggStream(AudioSource audioSource, CancellationToken cancellationToken)
    {
        var bufferSize = (int)Constants.Audio.MaxStreamDuration.TotalSeconds * Constants.Audio.Bitrate / 8;
        var stream = MemoryStreamManager.Default.GetStream(nameof(ElevenLabsOfflineTranscriber), bufferSize);
        var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(audioSource, cancellationToken);
        await foreach (var frame in byteFrameStream.ConfigureAwait(false))
            stream.Write(frame.Buffer);
        stream.Position = 0;
        return stream;
    }

    // Nested types

    private sealed class ElevenLabsResponse
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("language_code")] public string? LanguageCode { get; set; }
        [JsonPropertyName("language_probability")] public double LanguageProbability { get; set; }
        [JsonPropertyName("words")] public ElevenLabsWord[]? Words { get; set; }
    }

    private sealed class ElevenLabsWord
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("start")] public double Start { get; set; }
        [JsonPropertyName("end")] public double End { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
