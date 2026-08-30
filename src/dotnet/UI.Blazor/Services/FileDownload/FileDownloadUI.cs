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

    public Task Download(string blobId, string fileName, string contentType)
        => DownloadUrl(Hub.UrlMapper.ContentDownloadUrl(blobId), fileName, contentType);

    public async Task Download(IReadOnlyList<FileToSave> files)
    {
        if (files.Count == 0)
            return;

        if (FileSaver is { } fileSaver) {
            await fileSaver.Save(files).ConfigureAwait(false);
            return;
        }

        foreach (var file in files)
            await Hub.JS.InvokeVoidAsync(JSDownloadFileMethod, file.Url, file.FileName).ConfigureAwait(false);
    }

    public Task Preview(string blobId)
        => Hub.ExternalUrlOpener.Open(Hub.UrlMapper.ContentUrl(blobId));

    public Task DownloadUrl(string url, string fileName, string contentType)
        => Download([new FileToSave(url, fileName, contentType)]);
}
