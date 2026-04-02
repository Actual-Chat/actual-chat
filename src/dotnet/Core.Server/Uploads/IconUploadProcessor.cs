using ActualLab.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
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

        if (UniversalFormats.Contains(upload.ContentType))
            return await ProcessRaster(upload, progress, cancellationToken).ConfigureAwait(false);

        return await ConvertToPng(upload, progress, cancellationToken).ConfigureAwait(false);
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

    private async Task<ProcessedFile> ProcessRaster(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);

        var tempFile = await UploadProcessorHelper.DumpToTempFile(upload, cancellationToken).ConfigureAwait(false);
        try {
            var inputStream = await tempFile.Open().ConfigureAwait(false);
            await using var _ = inputStream.ConfigureAwait(false);

            using var image = await Image.LoadAsync(inputStream, cancellationToken).ConfigureAwait(false);
            progress?.Report(30);

            var resizeRequired = image.Width > MaxSize || image.Height > MaxSize;
            var imageProcessingRequired = image.Metadata.ExifProfile != null || resizeRequired;
            if (!imageProcessingRequired) {
                progress?.Report(100);
                return new ProcessedFile(tempFile, image.Size);
            }

            image.Mutate(img => {
                img.AutoOrient();
                if (resizeRequired)
                    img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(MaxSize) });
            });
            image.Metadata.ExifProfile = null;
            var imageSize = image.Size;
            progress?.Report(60);

            var outPath = (FilePath.GetApplicationTempDirectory() & upload.FileName)
                .ToUnique(randomLength: 10);
            var outStream = File.OpenWrite(outPath);
            await using (outStream.ConfigureAwait(false))
                await image.SaveAsync(outStream, image.Metadata.DecodedImageFormat!, cancellationToken).ConfigureAwait(false);

            tempFile.Delete();
            log.LogInformation(
                "Processed raster icon '{FileName}' ({Width}x{Height})",
                upload.FileName, imageSize.Width, imageSize.Height);
            progress?.Report(100);
            return new ProcessedFile(new UploadedTempFile(upload.FileName, upload.ContentType, outPath), imageSize);
        }
        catch {
            tempFile.Delete();
            throw;
        }
    }

    private async Task<ProcessedFile> ConvertToPng(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);

        var tempFile = await UploadProcessorHelper.DumpToTempFile(upload, cancellationToken).ConfigureAwait(false);
        try {
            var inputStream = await tempFile.Open().ConfigureAwait(false);
            await using var _ = inputStream.ConfigureAwait(false);

            using var image = await Image.LoadAsync(inputStream, cancellationToken).ConfigureAwait(false);
            progress?.Report(30);

            image.Mutate(img => {
                img.AutoOrient();
                if (image.Width > MaxSize || image.Height > MaxSize)
                    img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(MaxSize) });
            });
            image.Metadata.ExifProfile = null;
            var imageSize = image.Size;
            progress?.Report(60);

            var outPath = (FilePath.GetApplicationTempDirectory() & upload.FileName).ChangeExtension(".png")
                .ToUnique(randomLength: 10);
            var outStream = File.OpenWrite(outPath);
            await using (outStream.ConfigureAwait(false)) {
                await image.SaveAsync(outStream, new PngEncoder(), cancellationToken).ConfigureAwait(false);
            }

            log.LogInformation(
                "Converted '{ContentType}' icon '{FileName}' to PNG ({Width}x{Height})",
                upload.ContentType, upload.FileName, imageSize.Width, imageSize.Height);

            var converted = new UploadedTempFile(outPath.FileName, "image/png", outPath);
            progress?.Report(100);
            return new ProcessedFile(converted, imageSize);
        }
        finally {
            tempFile.Delete();
        }
    }

    private static (int Width, int Height) ComputeTargetSize(int width, int height)
    {
        if (width <= MaxSize && height <= MaxSize)
            return (width, height);

        var scale = Math.Min((float)MaxSize / width, (float)MaxSize / height);
        return ((int)(width * scale), (int)(height * scale));
    }
}
