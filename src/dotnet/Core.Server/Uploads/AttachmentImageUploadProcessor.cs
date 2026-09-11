using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ActualChat.Uploads;

/// <summary>
/// Chat attachment images arrive already resized and re-encoded by the client, so they are stored as-is:
/// only dimensions are read and metadata is stripped losslessly, unless the upload keeps it.
/// </summary>
public sealed class AttachmentImageUploadProcessor(IServiceProvider services) : IUploadProcessor
{
    private const long MaxStrippableLength = 64 * 1024 * 1024;

    private ILogger Log => field ??= services.LogFor(GetType());

    public bool Supports(string contentType, MediaKind mediaKind)
        => mediaKind == MediaKind.ChatEntryAttachment
            && MediaTypeExt.IsImage(contentType)
            && !MediaTypeExt.IsGif(contentType)
            && !MediaTypeExt.IsSvg(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        // A strippable upload is read once here: every Open() is another full download from blob storage
        var mustStrip = !upload.KeepMetadata && upload.Length <= MaxStrippableLength;
        var (imageInfo, data) = await Read(upload, mustStrip, cancellationToken).ConfigureAwait(false);
        if (imageInfo is null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        // Nothing is decoded here, so the bound is the client's 8K limit rather than the decode limit
        imageInfo.RequireWithinLimits(Constants.Attachments.MaxImagePixelCount);
        var size = GetDisplaySize(imageInfo);
        if (upload.KeepMetadata)
            return new ProcessedFile(upload, size);
        if (data is null) {
            Log.LogWarning("'{FileName}' is too large to strip metadata ({Length} bytes)",
                upload.FileName, upload.Length);
            return new ProcessedFile(upload, size);
        }

        var stripped = ImageMetadataStripper.Strip(data);
        if (ReferenceEquals(stripped, data))
            return new ProcessedFile(upload, size);

        var tempFilePath = UploadedFileExt.NewTempFilePath();
        await File.WriteAllBytesAsync(tempFilePath, stripped, cancellationToken).ConfigureAwait(false);
        var strippedFile = new UploadedTempFile(upload.GetDisplayFileName(), upload.ContentType, tempFilePath);
        return new ProcessedFile(strippedFile, size);
    }

    // Private methods

    private async Task<(ImageInfo? ImageInfo, byte[]? Data)> Read(
        UploadedFile upload,
        bool mustStrip,
        CancellationToken cancellationToken)
    {
        try {
            var stream = await upload.Open().ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            if (!mustStrip) {
                var streamed = await Image
                    .IdentifyAsync(ImageLimits.DecoderOptions, stream, cancellationToken)
                    .ConfigureAwait(false);
                return (streamed, null);
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var data = buffer.ToArray();
            return (Image.Identify(ImageLimits.DecoderOptions, data), data);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", upload.FileName);
            return (null, null);
        }
    }

    private static Size2D GetDisplaySize(ImageInfo imageInfo)
    {
        // The file keeps its EXIF Orientation, and 5-8 rotate the image by 90 degrees
        var orientation = imageInfo.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var value) == true
            ? value.Value
            : (ushort)1;
        return orientation is >= 5 and <= 8
            ? new Size2D(imageInfo.Height, imageInfo.Width)
            : new Size2D(imageInfo.Width, imageInfo.Height);
    }
}
