using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace ActualChat.Uploads;

public class RasterImageNormalizer(ILogger<RasterImageNormalizer> log)
{
    public async Task<ProcessedFile> Normalize(
        UploadedFile upload,
        int maxSize,
        bool convertToPng = false,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var imageInfo = await Identify(upload, cancellationToken).ConfigureAwait(false);
        imageInfo.RequireDecodable();

        var inputStream = await upload.Open().ConfigureAwait(false);
        await using var _ = inputStream.ConfigureAwait(false);

        using var image = await Image.LoadAsync(ImageLimits.DecoderOptions, inputStream, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(30);

        var changed = OrientAndResize(image, maxSize);
        progress?.Report(60);

        if (!changed && !convertToPng) {
            log.LogDebug("Image '{FileName}' needs no processing ({Width}x{Height})", upload.FileName, image.Width, image.Height);
            return new ProcessedFile(upload, new Size2D(image.Width, image.Height));
        }

        return await Save(upload, image, convertToPng, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImageInfo> Identify(UploadedFile upload, CancellationToken cancellationToken)
    {
        var stream = await upload.Open().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        return await Image.IdentifyAsync(ImageLimits.DecoderOptions, stream, cancellationToken).ConfigureAwait(false);
    }

    private static bool OrientAndResize(Image image, int maxSize)
    {
        var resizeRequired = image.Width > maxSize || image.Height > maxSize;
        if (!resizeRequired && image.Metadata.ExifProfile is null)
            return false;

        image.Mutate(img => {
            img.AutoOrient();
            if (resizeRequired)
                img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new SixLabors.ImageSharp.Size(maxSize) });
        });
        image.Metadata.ExifProfile = null;
        return true;
    }

    private async Task<ProcessedFile> Save(UploadedFile upload, Image image, bool convertToPng, CancellationToken cancellationToken)
    {
        var outPath = UploadedFileExt.NewTempFilePath();
        var outStream = File.OpenWrite(outPath);
        await using var _ = outStream.ConfigureAwait(false);
        var format = convertToPng ? PngFormat.Instance : image.Metadata.DecodedImageFormat!;
        await image.SaveAsync(outStream, format, cancellationToken).ConfigureAwait(false);
        var contentType = convertToPng ? "image/png" : upload.ContentType;

        log.LogInformation("Normalized '{FileName}' → {Width}x{Height}{ConvertNote}",
            upload.FileName, image.Width, image.Height,
            convertToPng ? " (converted to PNG)" : "");

        var displayedName = upload.GetDisplayFileName();
        if (convertToPng)
            displayedName = displayedName.ChangeExtension(".png");
        return new ProcessedFile(new UploadedTempFile(displayedName, contentType, outPath), new Size2D(image.Width, image.Height));
    }
}
