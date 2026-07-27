using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

public class ImageUploadProcessor(IServiceProvider services) : IUploadProcessor
{
    private ILogger Log => field ??= services.LogFor(GetType());
    private RasterImageNormalizer RasterImageNormalizer => field ??= services.GetRequiredService<RasterImageNormalizer>();

    public bool Supports(string contentType, MediaKind mediaKind)
        // GIF is passed through to preserve animation. Icon media kinds are handled by IconUploadProcessor.
        => MediaTypeExt.IsImage(contentType)
            && !MediaTypeExt.IsGif(contentType)
            && !MediaTypeExt.IsSvg(contentType)
            && !mediaKind.IsChatIcon;

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);

        var tempFile = await upload.DumpToTempFile(cancellationToken).ConfigureAwait(false);
        ProcessedFile processedFile;
        try {
            processedFile = await ProcessInternal(tempFile, progress, cancellationToken).ConfigureAwait(false);
        }
        catch {
            tempFile.Delete();
            throw;
        }
        if (processedFile.File != tempFile)
            tempFile.Delete();
        return processedFile;
    }

    private async Task<ProcessedFile> ProcessInternal(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var imageInfo = await GetImageInfo(upload).ConfigureAwait(false);
        if (imageInfo == null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        imageInfo.RequireWithinLimits();

        // Do not process GIFs and other animated images.
        if (imageInfo.FrameMetadataCollection.Count > 0
            || imageInfo.Metadata.TryGetGifMetadata(out _))
            return new ProcessedFile(upload, new Size2D(imageInfo.Width, imageInfo.Height));

        return await RasterImageNormalizer.Normalize(upload, 1920, progress: progress, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ImageInfo?> GetImageInfo(UploadedFile file)
    {
        try {
            var inputStream = await file.Open().ConfigureAwait(false);
            await using var __ = inputStream.ConfigureAwait(false);
            return await Image.IdentifyAsync(ImageLimits.DecoderOptions, inputStream).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }
}
