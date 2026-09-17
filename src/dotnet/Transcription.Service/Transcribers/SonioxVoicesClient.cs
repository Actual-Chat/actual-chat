using System.Net;
using System.Net.Http.Headers;

namespace ActualChat.Transcription;

/// <summary>
/// Soniox voice-cloning REST API: create/get/list/delete. The only <see cref="ISonioxVoices"/>
/// implementation; used by VoicePool (Streaming.Service) through that interface.
/// </summary>
public sealed class SonioxVoicesClient(SonioxClient sonioxClient) : ISonioxVoices
{
    private const string BaseUrl = "https://api.soniox.com/v1/voices";
    private const string ReadyStatus = "ready";
    private const string FailedStatus = "failed";
    private const int ListPageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private SonioxClient Client { get; } = sonioxClient;

    public async Task<SonioxVoice> Create(string name, Stream wav, CancellationToken cancellationToken)
    {
        using var httpClient = Client.CreateHttpClient();
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(wav);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "voice.wav");
        content.Add(new StringContent(name), "name");

        using var response = await httpClient.PostAsync(BaseUrl, content, cancellationToken).ConfigureAwait(false);
        await SonioxClient.EnsureSuccess(response, "voice create", cancellationToken).ConfigureAwait(false);
        var result = await response.Content
            .ReadFromJsonAsync<SonioxVoiceResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return ToVoice(result) ?? throw StandardError.External("Soniox returned no voice.");
    }

    public async Task<SonioxVoice?> Get(string id, CancellationToken cancellationToken)
    {
        using var httpClient = Client.CreateHttpClient();
        using var response = await httpClient.GetAsync($"{BaseUrl}/{id}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return null;

        await SonioxClient.EnsureSuccess(response, "voice get", cancellationToken).ConfigureAwait(false);
        var result = await response.Content
            .ReadFromJsonAsync<SonioxVoiceResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return ToVoice(result);
    }

    public async Task<ApiArray<SonioxVoice>> List(CancellationToken cancellationToken)
    {
        var voices = new List<SonioxVoice>();
        string? cursor = null;
        do {
            using var httpClient = Client.CreateHttpClient();
            var url = $"{BaseUrl}?limit={ListPageSize}";
            if (!cursor.IsNullOrEmpty())
                url += $"&cursor={Uri.EscapeDataString(cursor)}";

            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            await SonioxClient.EnsureSuccess(response, "voice list", cancellationToken).ConfigureAwait(false);
            var page = await response.Content
                .ReadFromJsonAsync<SonioxVoiceList>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            foreach (var voice in page?.Voices ?? [])
                if (ToVoice(voice) is { } v)
                    voices.Add(v);
            cursor = page?.NextPageCursor;
        } while (!cursor.IsNullOrEmpty());
        return voices.ToApiArray();
    }

    public async Task Delete(string id, CancellationToken cancellationToken)
    {
        using var httpClient = Client.CreateHttpClient();
        using var response = await httpClient
            .DeleteAsync($"{BaseUrl}/{id}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return;

        await SonioxClient.EnsureSuccess(response, "voice delete", cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static SonioxVoice? ToVoice(SonioxVoiceResponse? response)
    {
        if (response is not { Id.Length: > 0 })
            return null;

        var models = response.Models ?? [];
        var isReady = models.Length > 0 && models.All(m => m.Status == ReadyStatus);
        var isFailed = models.Any(m => m.Status == FailedStatus);
        return new SonioxVoice(response.Id, response.Name ?? "", isReady, isFailed);
    }
}
