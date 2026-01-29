using ActualChat.App.Maui.IosShareExt.Components;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Hashing;
using ActualChat.UI;
using ActualLab.IO;
using Microsoft.Maui.Storage;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class IconUI(IosHub hub) : UIServiceBase(hub), IComputeService
{
    private static readonly FilePath IconCacheDir = Path.Combine(FileSystem.CacheDirectory, "icons");

    private UrlMapper UrlMapper => Hub.UrlMapper;
    private HttpClient HttpClient => field ??= Hub.HttpClientFactory.CreateClient("Avatars");

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
    protected virtual async Task<LoadedImage?> GenerateAvatar(string key, AvatarKind kind, CancellationToken cancellationToken)
    {
        var filePath = GetCacheFilePath($"avatar:{kind}:{key}", ".png");
        if (File.Exists(filePath))
            return new LoadedImage(filePath, kind);

        var pngBytes = kind is AvatarKind.Marble
            ? MarbleAvatars.GeneratePng(key)
            : BeamAvatars.GeneratePng(key);
        EnsureIconCacheDir();
        await File.WriteAllBytesAsync(filePath, pngBytes, cancellationToken).ConfigureAwait(false);
        return new LoadedImage(filePath, kind);
    }

    private void SaveSvgAsPng(Stream svgStream, FilePath filePath)
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
