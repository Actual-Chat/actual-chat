using Stl.IO;

namespace ActualChat.Uploads;

public class ImageUploadProcessor(ILogger<ImageUploadProcessor> log) : IUploadProcessor
{
    private ILogger<ImageUploadProcessor> Log { get; } = log;

    public bool Supports(string contentType)
        => MediaTypeExt.IsImage(contentType);

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
        var outPath = FilePath.GetApplicationTempDirectory() & (Guid.NewGuid().ToString("N") + "_" + file.FileName);
        var outStream = File.OpenWrite(outPath);
        await using (var _ = outStream.ConfigureAwait(false)) {
            var inputStream = file.Open();
            await using var __ = inputStream.ConfigureAwait(false);
            using (Image image = await Image.LoadAsync(inputStream, cancellationToken).ConfigureAwait(false)) {
                image.Mutate(img => {
                    // https://github.com/SixLabors/ImageSharp/issues/790#issuecomment-447581798
                    img.AutoOrient();
                    if (resizeRequired)
                        img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(sizeLimit) });
                });
                image.Metadata.ExifProfile = null;
                imageSize = image.Size;
                await image.SaveAsync(outStream, image.Metadata.DecodedImageFormat!, cancellationToken: cancellationToken).ConfigureAwait(false);
                outStream.Position = 0;
            }
        }

        return new ProcessedFile(new UploadedTempFile(file.FileName, file.ContentType, outPath), imageSize);
    }

    private async Task<ImageInfo?> GetImageInfo(UploadedFile file)
    {
        try {
            var inputStream = file.Open();
            await using var __ = inputStream.ConfigureAwait(false);
            return await Image.IdentifyAsync(inputStream).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }
}
