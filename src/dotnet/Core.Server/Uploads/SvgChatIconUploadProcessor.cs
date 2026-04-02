using ActualLab.IO;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.Uploads;

public class SvgChatIconUploadProcessor(ILogger<SvgChatIconUploadProcessor> log) : IUploadProcessor
{
    private const int MaxSize = 1920;

    public bool Supports(string contentType, MediaKind mediaKind)
        => MediaTypeExt.IsSvg(contentType)
            && mediaKind is MediaKind.ChatPicture or MediaKind.UserPicture or MediaKind.UserAvatarPicture;

    public Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);

        using var inputStream = upload.Open().GetAwaiter().GetResult();
        using var svg = SKSvg.CreateFromStream(inputStream);
        var picture = svg.Picture
            ?? throw StandardError.Internal("Failed to parse SVG file.");

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw StandardError.Internal("SVG has invalid dimensions.");

        var (targetWidth, targetHeight) = ComputeTargetSize((int)bounds.Width, (int)bounds.Height);
        var scaleX = targetWidth / bounds.Width;
        var scaleY = targetHeight / bounds.Height;

        var imageInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scaleX, scaleY);
        canvas.DrawPicture(picture);

        var outFileName = Path.ChangeExtension(upload.FileName, ".png");
        var outPath = FilePath.GetApplicationTempDirectory()
            & (Guid.NewGuid().ToString("N") + "_" + FileExt.ShortenFileName(outFileName));

        using var pixmap = surface.PeekPixels();
        using var fileStream = new SKFileWStream(outPath);
        if (!pixmap.Encode(fileStream, SKEncodedImageFormat.Png, 100))
            throw StandardError.Internal("Failed to encode SVG as PNG.");

        log.LogInformation("Converted SVG '{FileName}' to PNG ({Width}x{Height})", upload.FileName, targetWidth, targetHeight);

        var converted = new UploadedTempFile(outFileName, "image/png", outPath);
        progress?.Report(100);
        return Task.FromResult(new ProcessedFile(converted, null));
    }

    private static (int Width, int Height) ComputeTargetSize(int width, int height)
    {
        if (width <= MaxSize && height <= MaxSize)
            return (width, height);

        var scale = Math.Min((float)MaxSize / width, (float)MaxSize / height);
        return ((int)(width * scale), (int)(height * scale));
    }
}
