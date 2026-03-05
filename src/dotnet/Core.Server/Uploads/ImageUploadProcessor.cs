using ActualChat.Media;
using ActualLab.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace ActualChat.Uploads;

public class ImageUploadProcessor(ILogger<ImageUploadProcessor> log) : IUploadProcessor
{
    private ILogger Log { get; } = log;

    public bool Supports(string contentType)
        // SVG is a vector format that ImageSharp can't process - pass through unchanged.
        => MediaTypeExt.IsImage(contentType) && !(MediaTypeExt.IsSvg(contentType) || MediaTypeExt.IsGif(contentType));

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        var tempFile = await UploadHelper.DumpToTempFile(upload, cancellationToken).ConfigureAwait(false);
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

        // Do not process GIFs and other animated images.
        if (imageInfo.FrameMetadataCollection.Count > 0
            || imageInfo.Metadata.TryGetGifMetadata(out _))
            return new ProcessedFile(upload, imageInfo.Size);

        const int sizeLimit = 1920;
        var resizeRequired = imageInfo.Height > sizeLimit || imageInfo.Width > sizeLimit;
        // Sometimes we can see that image preview is distorted.
        // This happens because image EXIF metadata contains information about image rotation
        // which is automatically applied by modern image viewers and browsers.
        // So we need to switch width and height to get appropriate size for image preview.
        var imageProcessingRequired = imageInfo.Metadata.ExifProfile != null || resizeRequired;
        if (!imageProcessingRequired)
            return new ProcessedFile(upload, imageInfo.Size);

        progress?.Report(20);
        Size imageSize;
        var outPath = FilePath.GetApplicationTempDirectory() & (Guid.NewGuid().ToString("N") + "_" + FileExt.ShortenFileName(upload.FileName));
        var outStream = File.OpenWrite(outPath);
        await using (var _ = outStream.ConfigureAwait(false)) {
            var inputStream = await upload.Open().ConfigureAwait(false);
            await using var __ = inputStream.ConfigureAwait(false);
            using (Image image = await Image.LoadAsync(inputStream, cancellationToken).ConfigureAwait(false)) {
                progress?.Report(50);
                image.Mutate(img => {
                    // https://github.com/SixLabors/ImageSharp/issues/790#issuecomment-447581798
                    img.AutoOrient();
                    if (resizeRequired)
                        img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(sizeLimit) });
                });
                image.Metadata.ExifProfile = null;
                imageSize = image.Size;
                progress?.Report(80);
                await image.SaveAsync(outStream, image.Metadata.DecodedImageFormat!, cancellationToken: cancellationToken).ConfigureAwait(false);
                outStream.Position = 0;
            }
        }

        return new ProcessedFile(new UploadedTempFile(upload.FileName, upload.ContentType, outPath), imageSize);
    }

    private async Task<ImageInfo?> GetImageInfo(UploadedFile file)
    {
        try {
            var inputStream = await file.Open().ConfigureAwait(false);
            await using var __ = inputStream.ConfigureAwait(false);
            // Decode only the first frame to identify whether it's an animated image or not.'
            var options = new DecoderOptions { MaxFrames = 1 };
            return await Image.IdentifyAsync(options, inputStream).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }
}
