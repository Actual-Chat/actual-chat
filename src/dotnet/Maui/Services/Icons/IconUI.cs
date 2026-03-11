using ActualChat.Hashing;
using ActualLab.IO;
using Microsoft.Maui.Storage;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.Maui.Services;

public class IconUI(IServiceProvider services) : ProcessorBase, IComputeService
{
    private static readonly FilePath IconCacheDir = Path.Combine(FileSystem.CacheDirectory, "icons");

    protected ILogger Log => field ??= services.LogFor(GetType());
    private UrlMapper UrlMapper => field ??= services.UrlMapper();
    private HttpClient HttpClient => field ??= services.HttpClientFactory().CreateClient("Avatars");

    public async Task<LoadedImage?> Get(IconQuery query, CancellationToken cancellationToken = default)
    {
        var url = UrlMapper.PicturePreview128Url(query.Picture);
        if (url.IsNullOrEmpty())
            return await GenerateAvatar(query.AvatarQuery, cancellationToken).ConfigureAwait(false);

        var filePath = await GetExternalImage(url, cancellationToken).ConfigureAwait(false);
        return filePath.IsEmpty ? null : new LoadedImage(filePath, null);
    }

    [ComputeMethod]
    protected virtual async Task<FilePath> GetExternalImage(string url, CancellationToken cancellationToken)
    {
        if (url.IsNullOrEmpty())
            return FilePath.Empty;

        try {
            var isSvg = url.OrdinalIgnoreCaseEndsWith(".svg");
            var ext = isSvg ? ".png" : Path.GetExtension(url);
            var filePath = GetCacheFilePath(url, ext);
            if (File.Exists(filePath))
                return filePath;

            var imgStream = await HttpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            await using var _1 = imgStream.ConfigureAwait(false);
            if (isSvg)
                SaveSvgAsPng(imgStream, filePath);
            else {
                EnsureIconCacheDir();
                var fileStream = File.Create(filePath);
                await using var _2 = fileStream.ConfigureAwait(false);
                await imgStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
            return filePath;
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "Failed to fetch external image: '{Url}'", url);
            return FilePath.Empty;
        }
    }

    [ComputeMethod]
    protected virtual async Task<LoadedImage?> GenerateAvatar(AvatarQuery query, CancellationToken cancellationToken)
    {
        var sSize = query.Size > 0 ? $"@{query.Size}" : "";
        var sTitle = !query.Title.IsNullOrEmpty() ? $"#{query.Title}" : "";
        var filePath = GetCacheFilePath($"avatar:{query.Kind}:{query.Key}{sSize}{sTitle}", ".png");
        if (File.Exists(filePath))
            return new LoadedImage(filePath, query.Kind);

        var url = UrlMapper.AvatarUrl(query);
        try {
            EnsureIconCacheDir();
            var imgStream = await HttpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            await using var _ = imgStream.ConfigureAwait(false);
            var fileStream = File.Create(filePath);
            await using var _2 = fileStream.ConfigureAwait(false);
            await imgStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            return new LoadedImage(filePath, query.Kind);
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "Failed to fetch avatar from API: '{Url}'", url);
            return null;
        }
    }

    private static void SaveSvgAsPng(Stream svgStream, FilePath filePath)
    {
        EnsureIconCacheDir();
        using var svg = SKSvg.CreateFromStream(svgStream);
        svg.Save(filePath, SKColor.Empty);
    }

    private static FilePath GetCacheFilePath(string key, string ext)
        => (IconCacheDir | key.Hash().SHA256().AlphaNumeric()).ChangeExtension(ext);

    private static void EnsureIconCacheDir()
    {
        if (!Directory.Exists(IconCacheDir))
            Directory.CreateDirectory(IconCacheDir);
    }
}
