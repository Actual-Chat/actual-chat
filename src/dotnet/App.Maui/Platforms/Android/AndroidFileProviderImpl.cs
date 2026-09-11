using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class AndroidFileProviderImpl : IMauiFileProviderImpl
{
    public AndroidFileProviderImpl(AndroidContentDownloader downloader, string uri)
    {
        Downloader = downloader;
        Uri = uri;
        AndroidFilePermissionsKeeper.Register(uri, this);
    }

    private string? _decodedUri;
    private AndroidContentDownloader Downloader { get; }
    private string Uri { get; }

    public Task WhenFileStreamReady()
        => Task.CompletedTask;

    public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
        => Task.FromResult(new FilePreview(AndroidContentDownloader.CreateWebRequestUri(Uri)));

    public Task PrepareForSaving()
    {
        // NOTE: Files with the "file://" scheme selected with FilePicker gives SecurityException
        // on an attempt to take persisted permission.
        // However, we can open such files on the next app launch.
        // So we can safely ignore this case.
        if (!Uri.StartsWith(System.Uri.UriSchemeFile))
            AndroidFilePermissionsKeeper.TakeReadPermission(Uri, this);
        return Task.CompletedTask;
    }

    public Task ClearBeforeRemoving()
    {
        AndroidFilePermissionsKeeper.ReleaseReadPermission(Uri, this);
        AndroidContentDownloader.DeleteCachedFile(Uri);
        DeleteDecodedFile();
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenRead()
    {
        var (stream, _) = Downloader.OpenInputStream(Uri);
        return Task.FromResult(stream);
    }

    public async Task<string> GetContentUrl(int? decodeMaxSize, CancellationToken cancellationToken)
    {
        if (decodeMaxSize is not { } maxSize)
            return AndroidContentDownloader.CreateWebRequestUri(Uri);

        var decodedUri = await AndroidHeifDecoder
            .TryDecodeToJpeg(Uri, maxSize, cancellationToken)
            .ConfigureAwait(false);
        if (decodedUri is null)
            return AndroidContentDownloader.CreateWebRequestUri(Uri);

        DeleteDecodedFile();
        _decodedUri = decodedUri;
        return AndroidContentDownloader.CreateWebRequestUri(decodedUri);
    }

    // Private methods

    private void DeleteDecodedFile()
    {
        if (_decodedUri is null)
            return;

        AndroidContentDownloader.DeleteCachedFile(_decodedUri);
        _decodedUri = null;
    }
}
