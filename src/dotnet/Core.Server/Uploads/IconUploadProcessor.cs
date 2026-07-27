using ActualLab.IO;

namespace ActualChat.Uploads;

/// <summary>
/// Handles all icon image uploads (chat/user/avatar pictures).
/// SVG → PNG via SkiaSharp; BMP → PNG via ImageSharp;
/// JPEG/PNG/WebP → resize/orient via ImageSharp (kept in original format).
/// </summary>
public class IconUploadProcessor(IServiceProvider services) : IUploadProcessor
{
    private const int MaxSize = Constants.Attachments.MaxIconSize;
    private ILogger Log => field ??= services.LogFor(GetType());
    private RasterImageNormalizer RasterImageNormalizer => field ??= services.GetRequiredService<RasterImageNormalizer>();
    private SvgRasterizer SvgRasterizer => field ??= services.GetRequiredService<SvgRasterizer>();

    public bool Supports(string contentType, MediaKind mediaKind)
        => mediaKind.IsChatIcon && MediaTypeExt.SupportedAvatarContentTypes.Contains(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (MediaTypeExt.IsSvg(upload.ContentType))
            return await ProcessSvg(upload, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(0);
        var convertToPng = !MediaTypeExt.AvatarPassthroughContentTypes.Contains(upload.ContentType);
        var tempFile = await upload.DumpToTempFile(cancellationToken).ConfigureAwait(false);
        ProcessedFile result;
        try {
            result = await RasterImageNormalizer.Normalize(tempFile, MaxSize, convertToPng, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch {
            tempFile.Delete();
            throw;
        }
        if (result.File != tempFile)
            tempFile.Delete();

        if (convertToPng)
            Log.LogInformation(
                "Converted '{ContentType}' icon '{FileName}' to PNG ({Width}x{Height})",
                upload.ContentType, upload.FileName, result.Size?.Width, result.Size?.Height);
        else if (result.File != tempFile)
            Log.LogInformation(
                "Processed raster icon '{FileName}' ({Width}x{Height})",
                upload.FileName, result.Size?.Width, result.Size?.Height);

        progress?.Report(100);
        return result;
    }

    private async Task<ProcessedFile> ProcessSvg(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);

        var svgStream = await upload.Open().ConfigureAwait(false);
        await using var _1 = svgStream.ConfigureAwait(false);

        var outPath = UploadedFileExt.NewTempFilePath();
        var pngStream = File.Create(outPath);
        await using var _2 = pngStream.ConfigureAwait(false);
        var size = SvgRasterizer.RasterizeToPng(svgStream, pngStream, MaxSize);

        var displayedName = upload.GetDisplayFileName().ChangeExtension(".png")
            .ToUnique(ensureNotExists: false, randomLength: 10).FileName;
        var converted = new UploadedTempFile(displayedName, "image/png", outPath);
        progress?.Report(100);
        return new ProcessedFile(converted, size);
    }
}
