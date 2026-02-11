using ActualChat.Hashing;
using ActualChat.UI;
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

    public async Task<LoadedImage?> Get(IconQuery iconQuery, CancellationToken cancellationToken = default)
    {
        var url = UrlMapper.PicturePreview128Url(iconQuery.Picture);
        if (!url.IsNullOrEmpty()) {
            var filePath = await GetExternalImage(url, cancellationToken).ConfigureAwait(false);
            return filePath.IsEmpty ? null : new LoadedImage(filePath, null);
        }

        return await GenerateAvatar(iconQuery.AvatarKey, iconQuery.AvatarKind, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<FilePath> GetExternalImage(string url, CancellationToken cancellationToken)
    {
        if (url.IsNullOrEmpty())
            return FilePath.Empty;

        try {
            var filePath = GetCacheFilePath(url, Path.GetExtension(url));
            if (File.Exists(filePath))
                return filePath;

            var imgStream = await HttpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            await using var _1 = imgStream.ConfigureAwait(false);
            if (url.OrdinalIgnoreCaseEndsWith(".svg"))
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
    protected virtual Task<LoadedImage?> GenerateAvatar(string key, AvatarKind kind, CancellationToken cancellationToken)
    {
        var filePath = GetCacheFilePath($"avatar:{kind}:{key}", ".png");
        if (File.Exists(filePath))
            return Task.FromResult<LoadedImage?>(new LoadedImage(filePath, kind));

        EnsureIconCacheDir();
        if (kind is AvatarKind.Marble)
            MarbleAvatars.GeneratePng(key, filePath);
        else
            BeamAvatars.GeneratePng(key, filePath);
        return Task.FromResult<LoadedImage?>(new LoadedImage(filePath, kind));
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
