using System.Net.Http.Json;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class FileUploader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UrlMapper _urlMapper;
    private readonly SessionTokens _sessionTokens;

    public async Task<MediaContent> Upload(ChatId chatId, Stream file, string? contentType, string? fileName, CancellationToken cancellationToken = default)
    {
        using var formData = new MultipartFormDataContent();
        var streamContent = new StreamContent(file);
        if (contentType != null)
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        formData.Add(streamContent, "file", fileName.NullIfEmpty() ?? "Upload");
        var httpClient = _httpClientFactory.CreateClient("UploadFile.Client");
        httpClient.DefaultRequestHeaders.Add(SessionTokens.HeaderName, _sessionTokens.Current!.Token);
        var url = _urlMapper.ApiBaseUrl + "chat-media/"+ chatId + "/upload";
        try {
            var response = await httpClient.PostAsync(url, formData, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode) {
                var result = await response.Content.ReadFromJsonAsync<MediaContent>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return result!;
            }
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new Exception(error);
        } catch(Exception) when (cancellationToken.IsCancellationRequested) {
            throw new TaskCanceledException();
        }
    }

    public FileUploader(
        IHttpClientFactory httpClientFactory,
        UrlMapper urlMapper,
        SessionTokens sessionTokens)
    {
        _httpClientFactory = httpClientFactory;
        _urlMapper = urlMapper;
        _sessionTokens = sessionTokens;
    }
}
