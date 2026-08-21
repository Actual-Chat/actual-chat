using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ActualChat.Module;

namespace ActualChat.Transcription;

/// <summary>
/// Soniox async REST API: uploads, transcription jobs, and the deletes that dispose of both.
/// Shared by <see cref="SonioxOfflineTranscriber"/> and <see cref="SonioxCleaner"/>.
/// </summary>
public sealed class SonioxClient(IServiceProvider services)
{
    public const string HttpClientName = nameof(SonioxClient);

    private const string BaseUrl = "https://api.soniox.com/v1";
    // Used when a 429 carries no Retry-After, and to cap the one it does carry
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private CoreServerSettings CoreServerSettings { get; } = services.GetRequiredService<CoreServerSettings>();
    private IHttpClientFactory HttpClientFactory { get; } = services.HttpClientFactory();

    public async Task<string> UploadFile(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        content.Add(fileContent, "file", fileName);

        using var response = await httpClient
            .PostAsync($"{BaseUrl}/files", content, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccess(response, "upload", cancellationToken).ConfigureAwait(false);
        var result = await response.Content
            .ReadFromJsonAsync<SonioxIdResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result?.Id ?? throw StandardError.External("Soniox returned no file id.");
    }

    public async Task<string> CreateTranscription(
        Dictionary<string, object?> request,
        CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();
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

    public async Task<SonioxStatusResponse> GetTranscriptionStatus(
        string transcriptionId,
        CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();
        var status = await httpClient
            .GetFromJsonAsync<SonioxStatusResponse>(
                $"{BaseUrl}/transcriptions/{transcriptionId}", JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return status ?? throw StandardError.External("Soniox returned no transcription status.");
    }

    public async Task<SonioxResponse?> GetTranscript(string transcriptionId, CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();
        return await httpClient
            .GetFromJsonAsync<SonioxResponse>(
                $"{BaseUrl}/transcriptions/{transcriptionId}/transcript", JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<SonioxTranscriptionList> ListTranscriptions(
        string? cursor,
        int maxCount,
        CancellationToken cancellationToken)
        // Both lists are scoped to the key's project, while the caps Soniox enforces are
        // organization-wide - so they list what we can clean, not everything that counts against them.
        => ListPage<SonioxTranscriptionList>("transcriptions", cursor, maxCount, cancellationToken);

    public Task<SonioxFileList> ListFiles(string? cursor, int maxCount, CancellationToken cancellationToken)
        => ListPage<SonioxFileList>("files", cursor, maxCount, cancellationToken);

    public Task DeleteTranscription(string transcriptionId, CancellationToken cancellationToken)
        => Delete($"transcriptions/{transcriptionId}", cancellationToken);

    public Task DeleteFile(string fileId, CancellationToken cancellationToken)
        => Delete($"files/{fileId}", cancellationToken);

    // Private methods

    private async Task<T> ListPage<T>(
        string path,
        string? cursor,
        int maxCount,
        CancellationToken cancellationToken)
    {
        // The parameter is "limit": an unknown one is ignored rather than rejected, which silently
        // pins the page size to Soniox's own default.
        using var httpClient = CreateHttpClient();
        var url = $"{BaseUrl}/{path}?limit={maxCount}";
        if (!cursor.IsNullOrEmpty())
            url += $"&cursor={Uri.EscapeDataString(cursor)}";

        using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccess(response, $"list of {path}", cancellationToken).ConfigureAwait(false);
        var result = await response.Content
            .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw StandardError.External($"Soniox returned no {path} list.");
    }

    private async Task Delete(string path, CancellationToken cancellationToken)
    {
        // NotFound is success: deleting a transcription cascades to its file, and the caller deletes
        // both. Anything else throws - the caller's queue is the only retry these deletes get, and
        // that includes the Conflict returned while a cancelled transcription is still processing.
        using var httpClient = CreateHttpClient();
        using var response = await httpClient
            .DeleteAsync($"{BaseUrl}/{path}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return;

        await EnsureSuccess(response, $"delete of {path}", cancellationToken).ConfigureAwait(false);
    }

    private HttpClient CreateHttpClient()
    {
        var apiKey = CoreServerSettings.SonioxKey;
        if (apiKey.IsNullOrEmpty())
            throw StandardError.Configuration("CoreSettings:SonioxKey is not set.");

        // Created per call, but the factory pools the underlying handler.
        var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpClient;
    }

    private static async Task EnsureSuccess(
        HttpResponseMessage response,
        string step,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Soniox rate-limits requests per minute per organization, and both storage caps answer with
        // the same status - so a 429 is transient here, and the callers that spend the budget in bulk
        // need it typed to tell it apart from the failures that are worth retrying immediately.
        if (response.StatusCode is HttpStatusCode.TooManyRequests)
            throw new RateLimitExceededException(GetRetryDelay(response));

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw StandardError.External($"Soniox {step} failed: {(int)response.StatusCode} {body}");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultRetryDelay);
        return delay <= TimeSpan.Zero ? DefaultRetryDelay : TimeSpanExt.Min(delay, MaxRetryDelay);
    }
}
