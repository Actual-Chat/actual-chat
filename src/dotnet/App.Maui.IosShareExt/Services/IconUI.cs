using ActualChat.App.Maui.IosShareExt.Components;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.UI;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class IconUI(IosHub hub) : UIServiceBase(hub), IComputeService
{
    private UrlMapper UrlMapper => Hub.UrlMapper;
    private HttpClient HttpClient => field ??= Hub.HttpClientFactory.CreateClient("Avatars");

    public async Task<LoadedImage?> Get(IconQuery iconQuery, CancellationToken cancellationToken = default)
    {
        var url = UrlMapper.PicturePreview128Url(iconQuery.Picture);
        if (!url.IsNullOrEmpty()) {
            var externalImage = await GetExternalImage(url, cancellationToken).ConfigureAwait(false);
            return externalImage is null ? null : new LoadedImage(externalImage, null);
        }

        return await GenerateAvatar(iconQuery.AvatarKey, iconQuery.AvatarKind, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<byte[]?> GetExternalImage(string url, CancellationToken cancellationToken)
    {
        if (url.IsNullOrEmpty())
            return null;

        var bytes = await HttpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        if (!url.OrdinalIgnoreCaseEndsWith(".svg") || bytes.Length == 0)
            return bytes;

        return await ConvertSvgToPng(bytes).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<LoadedImage?> GenerateAvatar(string key, AvatarKind kind, CancellationToken cancellationToken)
    {
        if (kind is AvatarKind.Marble)
            return new (MarbleAvatars.GeneratePng(key), kind);

        var svg = BeamAvatars.GenerateSvg(key);
        var xmlBytes = System.Text.Encoding.UTF8.GetBytes(svg);
        var bytes = await ConvertSvgToPng(xmlBytes).ConfigureAwait(false);
        return bytes is null || bytes.Length == 0 ? null : new (bytes, kind);
    }

    private async Task<byte[]?> ConvertSvgToPng(byte[] svgBytes)
    {
        var svgStream = new MemoryStream(svgBytes);
        await using var _ = svgStream.ConfigureAwait(false);
        using var svg = new SKSvg();
        if (svg.Load(svgStream) is null)
            return null;

        var pngStream = new MemoryStream();
        await using var _1 = pngStream.ConfigureAwait(false);
        svg.Save(pngStream, SKColor.Empty);
        return pngStream.ToArray();
    }
}
