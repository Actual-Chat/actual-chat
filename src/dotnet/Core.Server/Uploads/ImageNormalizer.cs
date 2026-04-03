using ActualLab.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace ActualChat.Uploads;

public class ImageNormalizer(ILogger<ImageNormalizer> log)
{
    public async Task<ProcessedFile> Normalize(
        UploadedFile upload,
        int maxSize,
        bool convertToPng = false,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inputStream = await upload.Open().ConfigureAwait(false);
        await using var _ = inputStream.ConfigureAwait(false);

        using var image = await Image.LoadAsync(inputStream, cancellationToken).ConfigureAwait(false);
        progress?.Report(30);

        var resizeRequired = image.Width > maxSize || image.Height > maxSize;
        var mustProcess = convertToPng || image.Metadata.ExifProfile != null || resizeRequired;
        if (!mustProcess) {
            log.LogDebug("Image '{FileName}' needs no processing ({Width}x{Height})",
                upload.FileName, image.Width, image.Height);
            return new ProcessedFile(upload, image.Size);
        }

        image.Mutate(img => {
            img.AutoOrient();
            if (resizeRequired)
                img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxSize) });
        });
        image.Metadata.ExifProfile = null;
        var imageSize = image.Size;
        progress?.Report(60);

        var outPath = (FilePath.GetApplicationTempDirectory() & upload.FileName).ToUnique(randomLength: 10);
        if (convertToPng)
            outPath = outPath.ChangeExtension(".png");
        var outStream = File.OpenWrite(outPath);
        await using var _1 = outStream.ConfigureAwait(false);
        var format = convertToPng ? PngFormat.Instance : image.Metadata.DecodedImageFormat!;
        await image.SaveAsync(outStream, format, cancellationToken).ConfigureAwait(false);
        var contentType = convertToPng ? "image/png" : upload.ContentType;

        log.LogInformation("Normalized '{FileName}' → {Width}x{Height}{ConvertNote}",
            upload.FileName, imageSize.Width, imageSize.Height,
            convertToPng ? " (converted to PNG)" : "");

        return new ProcessedFile(new UploadedTempFile(outPath.FileName, contentType, outPath), imageSize);
    }
}
