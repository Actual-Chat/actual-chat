using ActualLab.IO;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.Uploads;

/// <summary>
/// Handles all icon image uploads (chat/user/avatar pictures).
/// SVG → PNG via SkiaSharp; exotic raster formats (AVIF, WebP, HEIF, etc.) → PNG via ImageSharp;
/// JPEG/PNG → resize/orient via ImageSharp (kept in original format).
/// </summary>
public class IconUploadProcessor(ILogger<IconUploadProcessor> log) : IUploadProcessor
{
    private const int MaxSize = 1920;

    private static readonly HashSet<string> UniversalFormats = new(StringComparer.OrdinalIgnoreCase) {
        "image/jpeg",
        "image/png",
    };

    public bool Supports(string contentType, MediaKind mediaKind)
    {
        if (!mediaKind.IsChatIcon)
            return false;

        if (MediaTypeExt.IsSvg(contentType))
            return true;

        return MediaTypeExt.IsImage(contentType) && !MediaTypeExt.IsGif(contentType);
    }

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (MediaTypeExt.IsSvg(upload.ContentType))
            return ProcessSvg(upload, progress);

        progress?.Report(0);
        var convertToPng = !UniversalFormats.Contains(upload.ContentType);
        var tempFile = await UploadProcessorHelper.DumpToTempFile(upload, cancellationToken).ConfigureAwait(false);
        ProcessedFile result;
        try {
            result = await UploadProcessorHelper.ProcessRasterImage(tempFile, MaxSize, convertToPng, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch {
            tempFile.Delete();
            throw;
        }
        if (result.File != tempFile)
            tempFile.Delete();

        if (convertToPng)
            log.LogInformation(
                "Converted '{ContentType}' icon '{FileName}' to PNG ({Width}x{Height})",
                upload.ContentType, upload.FileName, result.Size?.Width, result.Size?.Height);
        else if (result.File != tempFile)
            log.LogInformation(
                "Processed raster icon '{FileName}' ({Width}x{Height})",
                upload.FileName, result.Size?.Width, result.Size?.Height);

        progress?.Report(100);
        return result;
    }

    private ProcessedFile ProcessSvg(UploadedFile upload, IProgress<double>? progress)
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

        var outPath = (FilePath.GetApplicationTempDirectory() & upload.FileName).ChangeExtension(".png")
            .ToUnique(randomLength: 10);

        using var pixmap = surface.PeekPixels();
        using var fileStream = new SKFileWStream(outPath);
        if (!pixmap.Encode(fileStream, SKEncodedImageFormat.Png, 100))
            throw StandardError.Internal("Failed to encode SVG as PNG.");

        log.LogInformation("Converted SVG '{FileName}' to PNG ({Width}x{Height})", upload.FileName, targetWidth, targetHeight);

        var converted = new UploadedTempFile(outPath.FileName, "image/png", outPath);
        progress?.Report(100);
        return new ProcessedFile(converted, null);
    }

    private static (int Width, int Height) ComputeTargetSize(int width, int height)
    {
        if (width <= MaxSize && height <= MaxSize)
            return (width, height);

        var scale = Math.Min((float)MaxSize / width, (float)MaxSize / height);
        return ((int)(width * scale), (int)(height * scale));
    }
}
