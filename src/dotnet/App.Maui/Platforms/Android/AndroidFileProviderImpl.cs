using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class AndroidFileProviderImpl(AndroidContentDownloader downloader, string uri) : IMauiFileProviderImpl
{
    private AndroidContentDownloader Downloader { get; } = downloader;
    private string Uri { get; } = uri;

    public Task<string> GetPreviewUrl()
        => Task.FromResult(AndroidContentDownloader.CreateWebRequestUri(Uri));

    public Task PrepareForSaving()
        => Task.CompletedTask;

    public Task ClearBeforeRemoving()
        => Task.CompletedTask;

    public Task<Stream?> OpenRead()
    {
        var (stream, _) = Downloader.OpenInputStream(Uri);
        return Task.FromResult(stream);
    }
}
