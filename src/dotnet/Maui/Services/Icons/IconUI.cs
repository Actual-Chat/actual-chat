using ActualChat.Hashing;
using ActualLab.IO;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui.Services;

public class IconUI(IServiceProvider services) : ProcessorBase, IComputeService
{
    private static readonly FilePath IconCacheDir = Path.Combine(FileSystem.CacheDirectory, "icons");

    protected ILogger Log => field ??= services.LogFor(GetType());
    private UrlMapper UrlMapper => field ??= services.UrlMapper();
    private HttpClient HttpClient => field ??= services.HttpClientFactory().CreateClient("Avatars");

    [ComputeMethod]
    public virtual async Task<LoadedImage?> Get(IconQuery query, CancellationToken cancellationToken = default)
    {
        var (url, kind) = GetIconUrl(query);
        var filePath = await FetchImage(url, cancellationToken).ConfigureAwait(false);
        return filePath.IsEmpty ? null : new LoadedImage(filePath, kind);
    }

    private (string Url, AvatarKind? Kind) GetIconUrl(IconQuery query)
    {
        var pictureUrl = UrlMapper.PicturePreview128Url(query.Picture);
        return pictureUrl.IsNullOrEmpty()
            ? (UrlMapper.AvatarUrl(query.AvatarQuery), query.AvatarQuery.Kind)
            : (pictureUrl, null);
    }

    private Task<FilePath> FetchImage(string url, CancellationToken cancellationToken)
    {
        if (url.IsNullOrEmpty())
            return Task.FromResult(FilePath.Empty);

        var ext = Path.GetExtension(url);
        var filePath = GetCacheFilePath(url, ext);
        return FetchToCache(url, filePath, cancellationToken);
    }

    private async Task<FilePath> FetchToCache(string url, FilePath filePath, CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
            return filePath;

        try {
            var stream = await HttpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            EnsureIconCacheDir();
            await stream.CopyToFile(filePath, cancellationToken).ConfigureAwait(false);
            return filePath;
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "Failed to fetch image: '{Url}'", url);
            return FilePath.Empty;
        }
    }

    private static FilePath GetCacheFilePath(string key, string ext)
        => (IconCacheDir | key.Hash().SHA256().AlphaNumeric()).ChangeExtension(ext);

    private static void EnsureIconCacheDir()
    {
        if (!Directory.Exists(IconCacheDir))
            Directory.CreateDirectory(IconCacheDir);
    }
}
