using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ActualChat.Uploads;

/// <summary>
/// Chat attachment images arrive already resized and re-encoded by the client, so they are stored as-is:
/// only dimensions are read and metadata is stripped losslessly, unless the upload keeps it.
/// A GIF is read for its dimensions and then left completely alone - stripping would re-encode it
/// and drop the animation.
/// </summary>
public sealed class AttachmentImageUploadProcessor(IServiceProvider services) : IUploadProcessor
{
    private const long MaxStrippableLength = 64 * 1024 * 1024;

    private ILogger Log => field ??= services.LogFor(GetType());

    public bool Supports(string contentType, MediaKind mediaKind)
        => mediaKind == MediaKind.ChatEntryAttachment
            && MediaTypeExt.IsImage(contentType)
            && !MediaTypeExt.IsSvg(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        var isGif = MediaTypeExt.IsGif(upload.ContentType);
        var isHeif = MediaTypeExt.IsHeif(upload.ContentType);
        var isStrippableLength = upload.Length <= MaxStrippableLength;
        // A strippable upload is read once here: every Open() is another full download from blob storage.
        // A HEIF is always buffered too - ImageSharp can't open it, so its size comes from its boxes.
        var mustStrip = !isGif && !upload.KeepMetadata && isStrippableLength;
        var mustBuffer = mustStrip || (isHeif && isStrippableLength);
        var (readSize, data) = await Read(upload, isHeif, mustBuffer, cancellationToken).ConfigureAwait(false);
        if (readSize is not { } size)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        // Nothing is decoded here, so the bound is the stored-image limit rather than the decode limit
        var isWithinBounds = size.Width <= Constants.Attachments.MaxImageSize
            && size.Height <= Constants.Attachments.MaxImageSize
            && (long)size.Width * size.Height <= Constants.Attachments.MaxImagePixelCount;
        if (!isWithinBounds) {
            // Storing it as a file keeps the bytes the sender chose; rejecting after Send would not
            Log.LogInformation("'{FileName}': {Width}x{Height} exceeds the stored-image bounds, keeping it as a file",
                upload.FileName, size.Width, size.Height);
            var oversizedFile = data is null || upload.KeepMetadata
                ? upload
                : await StripMetadata(upload, data, cancellationToken).ConfigureAwait(false);
            return new ProcessedFile(oversizedFile.AsBinaryFile(), null);
        }

        if (isGif)
            return new ProcessedFile(upload, size);
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

    private async Task<(Size2D? Size, byte[]? Data)> Read(
        UploadedFile upload,
        bool isHeif,
        bool mustBuffer,
        CancellationToken cancellationToken)
    {
        try {
            var stream = await upload.Open().ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            if (!mustBuffer) {
                if (isHeif) {
                    Log.LogWarning("'{FileName}' is too large to read as HEIF ({Length} bytes)",
                        upload.FileName, upload.Length);
                    return (null, null);
                }

                var streamed = await Image
                    .IdentifyAsync(ImageLimits.DecoderOptions, stream, cancellationToken)
                    .ConfigureAwait(false);
                return (GetDisplaySize(streamed), null);
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var data = buffer.ToArray();
            // The content type is only what the sender claimed, so a mislabelled file still gets identified
            var heifSize = isHeif ? HeifReader.ReadDisplaySize(data) : null;
            return (heifSize ?? GetDisplaySize(Image.Identify(ImageLimits.DecoderOptions, data)), data);
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
