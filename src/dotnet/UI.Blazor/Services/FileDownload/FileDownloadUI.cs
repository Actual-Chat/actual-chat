using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// The single entry point for every "Download" / "Save" action in the UI.
/// Never navigates: a navigation-based download is hijacked by MAUI's URL routing
/// and dropped by WebViews, which register no native download handler.
/// </summary>
public sealed class FileDownloadUI(UIHub hub)
{
    private static readonly string JSDownloadFileMethod = $"{BlazorUICoreModule.ImportName}.downloadFile";

    private UIHub Hub { get; } = hub;
    private IFileSaver? FileSaver { get; } = hub.Services.GetService<IFileSaver>();
    private IExternalUrlOpener? UrlOpener { get; } = hub.Services.GetService<IExternalUrlOpener>();

    public Task Download(string blobId, string fileName, string contentType)
        => DownloadUrl(Hub.UrlMapper.ContentDownloadUrl(blobId), fileName, contentType);

    public Task Preview(string blobId)
    {
        var url = Hub.UrlMapper.ContentUrl(blobId);
        return UrlOpener is { } urlOpener
            ? urlOpener.Open(url)
            : Hub.JS.InvokeVoidAsync("open", url, "_blank", "noopener").AsTask();
    }

    public Task DownloadUrl(string url, string fileName, string contentType)
        => FileSaver is { } fileSaver
            ? fileSaver.Save(url, fileName, contentType)
            : Hub.JS.InvokeVoidAsync(JSDownloadFileMethod, url, fileName).AsTask();
}
