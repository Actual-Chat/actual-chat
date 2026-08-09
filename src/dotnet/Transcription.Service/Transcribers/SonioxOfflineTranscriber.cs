using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ActualChat.Audio;
using ActualChat.Module;
using Microsoft.IO;

namespace ActualChat.Transcription;

/// <summary>
/// One-shot transcriber built on Soniox's async REST API:
/// upload the audio, create a transcription, poll, then fetch the transcript.
/// </summary>
public sealed class SonioxOfflineTranscriber : IOfflineTranscriber
{
    private const string BaseUrl = "https://api.soniox.com/v1";
    private const string Model = "stt-async-v5";
    private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private CoreServerSettings CoreServerSettings { get; }
    private IHttpClientFactory HttpClientFactory { get; }
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
        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
        HttpClientFactory = services.HttpClientFactory();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
    }

    public async Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        // Created per call, but the factory pools the underlying handler - this runs once per voice message.
        var httpClient = HttpClientFactory.CreateClient(nameof(SonioxOfflineTranscriber));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        string? fileId = null;
        string? transcriptionId = null;
        try {
            fileId = await UploadAudio(httpClient, audioSource, cancellationToken).ConfigureAwait(false);
            transcriptionId = await CreateTranscription(httpClient, fileId, options, audioSource, cancellationToken)
                .ConfigureAwait(false);
            await WaitForCompletion(httpClient, transcriptionId, cancellationToken).ConfigureAwait(false);
            return await GetTranscript(httpClient, transcriptionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Soniox offline transcription failed");
            return null;
        }
        finally {
            // Cleanup is fire-and-forget to keep two Soniox round-trips off the transcription latency path.
            // It takes over httpClient here and disposes it once the deletes are done.
            _ = BackgroundTask.Run(() => Cleanup(httpClient, transcriptionId, fileId),
                Log,
                "Soniox cleanup failed",
                CancellationToken.None);
        }
    }

    // Private methods

    private async Task Cleanup(HttpClient httpClient, string? transcriptionId, string? fileId)
    {
        // Soniox caps the stored file count per organization, so uploads must be dropped even when transcription fails.
        // Deleting the transcription also deletes its associated files, hence the extra NotFound tolerance below.
        using (httpClient) {
            if (transcriptionId != null)
                await Delete(httpClient, $"transcriptions/{transcriptionId}").ConfigureAwait(false);
            if (fileId != null)
                await Delete(httpClient, $"files/{fileId}").ConfigureAwait(false);
        }
    }

    private async Task Delete(HttpClient httpClient, string path)
    {
        // Cleanup runs from a finally block, so it uses CancellationToken.None and never throws.
        // NotFound means the transcription delete already cascaded to the file; Conflict means the
        // transcription is still processing, which is expected when the caller cancelled mid-flight.
        try {
            using var response = await httpClient
                .DeleteAsync($"{BaseUrl}/{path}", CancellationToken.None)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Conflict))
                Log.LogWarning("Soniox cleanup of {Path} failed: {StatusCode}", path, (int)response.StatusCode);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Soniox cleanup of {Path} failed", path);
        }
    }

    private async Task<string> UploadAudio(
        HttpClient httpClient,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var stream = await ToOggStream(audioSource, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false)) {
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
            content.Add(fileContent, "file", "speech.ogg");

            using var response = await httpClient
                .PostAsync($"{BaseUrl}/files", content, cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccess(response, "upload", cancellationToken).ConfigureAwait(false);
            var result = await response.Content
                .ReadFromJsonAsync<SonioxIdResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result?.Id ?? throw StandardError.External("Soniox returned no file id.");
        }
    }

    private async Task<string> CreateTranscription(
        HttpClient httpClient,
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
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient
            .PostAsync($"{BaseUrl}/transcriptions", content, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccess(response, "create transcription", cancellationToken).ConfigureAwait(false);
        var result = await response.Content
            .ReadFromJsonAsync<SonioxIdResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result?.Id ?? throw StandardError.External("Soniox returned no transcription id.");
    }

    private async Task WaitForCompletion(
        HttpClient httpClient,
        string transcriptionId,
        CancellationToken cancellationToken)
    {
        while (true) {
            var status = await httpClient
                .GetFromJsonAsync<SonioxStatusResponse>(
                    $"{BaseUrl}/transcriptions/{transcriptionId}", JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (status == null)
                throw StandardError.External("Soniox returned no transcription status.");
            if (status.Status == "completed")
                return;
            if (status.Status == "error")
                throw StandardError.External($"Soniox transcription failed: {status.ErrorMessage}");

            await Clocks.CpuClock.Delay(PollPeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Transcript?> GetTranscript(
        HttpClient httpClient,
        string transcriptionId,
        CancellationToken cancellationToken)
    {
        var response = await httpClient
            .GetFromJsonAsync<SonioxResponse>(
                $"{BaseUrl}/transcriptions/{transcriptionId}/transcript", JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (response?.Tokens is not { Length: > 0 } tokens)
            return null;

        // The async API returns the whole transcript at once, and its tokens carry no is_final flag.
        var builder = new SonioxTranscriptBuilder();
        foreach (var token in tokens)
            token.IsFinal = true;
        builder.Update(tokens);
        return builder.Complete();
    }

    private static async Task EnsureSuccess(
        HttpResponseMessage response,
        string step,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw StandardError.External($"Soniox {step} failed: {(int)response.StatusCode} {body}");
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
