namespace ActualChat.Uploads;

public class ImageUploadProcessor(ILogger<ImageUploadProcessor> log) : IUploadProcessor
{
    private ILogger<ImageUploadProcessor> Log { get; } = log;

    public bool Supports(FileInfo file)
        => file.ContentType.OrdinalIgnoreCaseContains("image");

    public async Task<ProcessedFileInfo> Process(FileInfo file, CancellationToken cancellationToken)
    {
        var imageInfo = await GetImageInfo(file).ConfigureAwait(false);
        if (imageInfo == null) {
            var fileInfo = file with {
                ContentType = System.Net.Mime.MediaTypeNames.Application.Octet,
            };
            return new ProcessedFileInfo(fileInfo, null);
        }

        const int sizeLimit = 1920;
        var resizeRequired = imageInfo.Height > sizeLimit || imageInfo.Width > sizeLimit;
        // Sometimes we can see that image preview is distorted.
        // This happens because image EXIF metadata contains information about image rotation
        // which is automatically applied by modern image viewers and browsers.
        // So we need to switch width and height to get appropriate size for image preview.
        var imageProcessingRequired = imageInfo.Metadata.ExifProfile != null || resizeRequired;
        if (!imageProcessingRequired)
            return new ProcessedFileInfo(file, imageInfo.Size);

        Size imageSize;
        byte[] content;
        var targetStream = new MemoryStream(file.Content.Length);
        await using (var _ = targetStream.ConfigureAwait(false))
        using (Image image = Image.Load(file.Content)) {
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
            content = targetStream.ToArray();
        }

        return new ProcessedFileInfo(file with { Content = content }, imageSize);
    }

    private async Task<ImageInfo?> GetImageInfo(FileInfo file)
    {
        try {
            using var stream = new MemoryStream(file.Content);
            var imageInfo = await Image.IdentifyAsync(stream).ConfigureAwait(false);
            return imageInfo;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }
}
