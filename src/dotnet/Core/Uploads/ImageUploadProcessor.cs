namespace ActualChat.Uploads;

public class ImageUploadProcessor(ILogger<ImageUploadProcessor> log) : IUploadProcessor
{
    private ILogger<ImageUploadProcessor> Log { get; } = log;

    public bool Supports(UploadedFile file)
        => file.ContentType.OrdinalIgnoreCaseContains("image");

    public async Task<ProcessedFile> Process(UploadedFile file, CancellationToken cancellationToken)
    {
        var imageInfo = await GetImageInfo(file).ConfigureAwait(false);
        if (imageInfo == null) {
            var fileInfo = file with {
                ContentType = System.Net.Mime.MediaTypeNames.Application.Octet,
            };
            return new ProcessedFile(fileInfo, null);
        }

        const int sizeLimit = 1920;
        var resizeRequired = imageInfo.Height > sizeLimit || imageInfo.Width > sizeLimit;
        // Sometimes we can see that image preview is distorted.
        // This happens because image EXIF metadata contains information about image rotation
        // which is automatically applied by modern image viewers and browsers.
        // So we need to switch width and height to get appropriate size for image preview.
        var imageProcessingRequired = imageInfo.Metadata.ExifProfile != null || resizeRequired;
        if (!imageProcessingRequired)
            return new ProcessedFile(file, imageInfo.Size);

        Size imageSize;
        var targetFilePath = Path.ChangeExtension(file.TempFilePath, ".converted" + Path.GetExtension(file.TempFilePath));
        var targetStream = File.OpenWrite(targetFilePath);
        await using (var _ = targetStream.ConfigureAwait(false))
        using (Image image = await Image.LoadAsync(file.TempFilePath, cancellationToken).ConfigureAwait(false)) {
            image.Mutate(img => {
                // https://github.com/SixLabors/ImageSharp/issues/790#issuecomment-447581798
                img.AutoOrient();
                if (resizeRequired)
                    img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(sizeLimit) });
            });
            image.Metadata.ExifProfile = null;
            imageSize = image.Size;
            await image.SaveAsync(targetStream, image.Metadata.DecodedImageFormat!, cancellationToken: cancellationToken).ConfigureAwait(false);
            targetStream.Position = 0;
        }

        return new ProcessedFile(file with { TempFilePath = targetFilePath }, imageSize);
    }

    private async Task<ImageInfo?> GetImageInfo(UploadedFile file)
    {
        try {
            return await Image.IdentifyAsync(file.TempFilePath).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }
}
