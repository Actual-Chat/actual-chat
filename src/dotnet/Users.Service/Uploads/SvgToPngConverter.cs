using ActualChat.Uploads;
using ActualLab.IO;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.Users.Uploads;

public sealed class SvgToPngConverter(ILogger<SvgToPngConverter> log)
{
    private const int MaxSize = 1920;

    public ProcessedFile Convert(UploadedFile upload)
    {
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
            & (Guid.NewGuid().ToString("N") + "_" + ActualChat.Uploads.FileExt.ShortenFileName(outFileName));

        using var pixmap = surface.PeekPixels();
        using var fileStream = new SKFileWStream(outPath);
        if (!pixmap.Encode(fileStream, SKEncodedImageFormat.Png, 100))
            throw StandardError.Internal("Failed to encode SVG as PNG.");

        log.LogInformation("Converted SVG '{FileName}' to PNG ({Width}x{Height})", upload.FileName, targetWidth, targetHeight);

        var tempFile = new UploadedTempFile(outFileName, "image/png", outPath);
        var size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight);
        return new ProcessedFile(tempFile, size);
    }

    private static (int Width, int Height) ComputeTargetSize(int width, int height)
    {
        if (width <= MaxSize && height <= MaxSize)
            return (width, height);

        var scale = Math.Min((float)MaxSize / width, (float)MaxSize / height);
        return ((int)(width * scale), (int)(height * scale));
    }
}
