using System.Net.Http.Json;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class FileUploader(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private SessionTokens SessionTokens => Hub.SessionTokens;
    private IHttpClientFactory HttpClientFactory => Hub.HttpClientFactory;

    [RequiresUnreferencedCode("Uses ReadFromJsonAsync")]
    public FileUploadOperation CreateUploadOperation(ChatId chatId, Stream file, string? contentType, string? fileName)
    {
        var progress = new Progress<double>();
        return new FileUploadOperation(token => {
            HttpContent streamContent = new StreamContentWithProgress(file, progress, token); //new StreamContent(file);
            return UploadInternal(chatId,
                streamContent,
                contentType,
                fileName,
                token);
        }, progress);
    }

    [RequiresUnreferencedCode("Uses ReadFromJsonAsync")]
    private async Task<MediaContent> UploadInternal(ChatId chatId, HttpContent httpContent, string? contentType, string? fileName, CancellationToken cancellationToken)
    {
        using var formData = new MultipartFormDataContent();
        if (!contentType.IsNullOrEmpty())
            httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        formData.Add(httpContent, "file", fileName.NullIfEmpty() ?? "Upload");

        var httpClient = HttpClientFactory.CreateClient("UploadFile.Client");
        if (HostInfo.HostKind.IsApp()) {
            var sessionToken = await SessionTokens.Get(cancellationToken).ConfigureAwait(false);
            // TODO: review default session header configuration at ActualChat.UI.Blazor.App.AppStartup restEase.ConfigureHttpClient
            httpClient.DefaultRequestHeaders.Remove(SessionTokens.HeaderName);
            httpClient.DefaultRequestHeaders.Add(SessionTokens.HeaderName, sessionToken.Token);
        }

        var url = UrlMapper.ApiBaseUrl + "chat-media/"+ chatId + "/upload";
        try {
            var response = await httpClient.PostAsync(url, formData, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode) {
                var result = await response.Content
                    .ReadFromJsonAsync<MediaContent>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return result!;
            }
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var error = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase.NullIfEmpty() ?? response.StatusCode.ToInvariantString()}). Body: {errorBody}";
            throw StandardError.External(error);
        } catch(Exception) when (cancellationToken.IsCancellationRequested) {
            throw new TaskCanceledException();
        }
    }
}
