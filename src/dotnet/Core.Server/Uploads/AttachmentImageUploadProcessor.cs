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

        // Nothing is decoded here, so the bound is the stored-image limit rather than the decode limit
        var isWithinBounds = imageInfo.Width <= Constants.Attachments.MaxImageSize
            && imageInfo.Height <= Constants.Attachments.MaxImageSize
            && (long)imageInfo.Width * imageInfo.Height <= Constants.Attachments.MaxImagePixelCount;
        if (!isWithinBounds) {
            // Storing it as a file keeps the bytes the sender chose; rejecting after Send would not
            Log.LogInformation("'{FileName}': {Width}x{Height} exceeds the stored-image bounds, keeping it as a file",
                upload.FileName, imageInfo.Width, imageInfo.Height);
            var oversizedFile = data is null
                ? upload
                : await StripMetadata(upload, data, cancellationToken).ConfigureAwait(false);
            return new ProcessedFile(oversizedFile.AsBinaryFile(), null);
        }

        var size = GetDisplaySize(imageInfo);
        if (upload.KeepMetadata)
            return new ProcessedFile(upload, size);
        if (data is null) {
            Log.LogWarning("'{FileName}' is too large to strip metadata ({Length} bytes)",
                upload.FileName, upload.Length);
            return new ProcessedFile(upload, size);
        }

        var strippedFile = await StripMetadata(upload, data, cancellationToken).ConfigureAwait(false);
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

    private static async Task<UploadedFile> StripMetadata(
        UploadedFile upload,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var stripped = ImageMetadataStripper.Strip(data);
        if (ReferenceEquals(stripped, data))
            return upload;

        var tempFilePath = UploadedFileExt.NewTempFilePath();
        await File.WriteAllBytesAsync(tempFilePath, stripped, cancellationToken).ConfigureAwait(false);
        return new UploadedTempFile(upload.GetDisplayFileName(), upload.ContentType, tempFilePath);
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
