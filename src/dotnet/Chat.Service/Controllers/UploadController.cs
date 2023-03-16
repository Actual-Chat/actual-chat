using ActualChat.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class UploadController : ControllerBase
{
    private IContentSaver ContentSaver { get; }
    private ILogger<UploadController> Log { get; }
    private ICommander Commander { get; }

    public UploadController(
        IContentSaver contentSaver,
        ILogger<UploadController> log,
        ICommander commander)
    {
        ContentSaver = contentSaver;
        Log = log;
        Commander = commander;
    }

    [HttpPost, Route("api/chats/upload-picture")]
    public async Task<ActionResult<MediaContent>> UploadPicture(CancellationToken cancellationToken)
    {
        var httpRequest = HttpContext.Request;
        if (!httpRequest.HasFormContentType || httpRequest.Form.Files.Count == 0)
            return BadRequest("No file content found");

        if (httpRequest.Form.Files.Count > 1)
            return BadRequest("Too many files");

        var file = httpRequest.Form.Files[0];
        if (file.Length == 0)
            return BadRequest("Image is empty");

        if (file.Length > Constants.Chat.PictureFileSizeLimit)
            return BadRequest("Image is too big");

        var fileInfo = await ReadFileContent(file, cancellationToken).ConfigureAwait(false);
        var (processedFile, imageSize) = await ProcessFile(fileInfo, cancellationToken).ConfigureAwait(false);

        var mediaId = new MediaId(Ulid.NewUlid().ToString());
        var media = new Media.Media(mediaId) {
            ContentId = $"media/chats/{mediaId}{Path.GetExtension(file.FileName)}",
            FileName = fileInfo.FileName,
            Length = fileInfo.Length,
            ContentType = fileInfo.ContentType,
            Width = imageSize?.Width ?? 0,
            Height = imageSize?.Height ?? 0,
        };

        var changeCommand = new IMediaBackend.ChangeCommand(
            mediaId,
            new Change<Media.Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        using var stream = new MemoryStream(processedFile.Content);
        var content = new Content(media.ContentId, file.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        return Ok(new MediaContent(media.Id, media.ContentId));
    }

    private async Task<ProcessedFileInfo> ProcessFile(FileInfo file, CancellationToken cancellationToken)
    {
        if (!file.ContentType.OrdinalIgnoreCaseContains("image"))
            return new ProcessedFileInfo(file, null);

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
            return new ProcessedFileInfo(file, imageInfo.Size());

        Size imageSize;
        byte[] content;
        var targetStream = new MemoryStream(file.Content.Length);
        await using (var _ = targetStream.ConfigureAwait(false))
        using (Image image = Image.Load(SixLabors.ImageSharp.Configuration.Default, file.Content, out var imageFormat)) {
            image.Mutate(img => {
                // https://github.com/SixLabors/ImageSharp/issues/790#issuecomment-447581798
                img.AutoOrient();
                if (resizeRequired)
                    img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(sizeLimit) });
            });
            image.Metadata.ExifProfile = null;
            imageSize = image.Size();
            await image.SaveAsync(targetStream, imageFormat, cancellationToken: cancellationToken).ConfigureAwait(false);
            targetStream.Position = 0;
            content = targetStream.ToArray();
        }

        return new ProcessedFileInfo(file with { Content = content }, imageSize);
    }

    private async Task<IImageInfo?> GetImageInfo(FileInfo file)
    {
        try {
            using var stream = new MemoryStream(file.Content);
            var imageInfo = await Image.IdentifyAsync(stream).ConfigureAwait(false);
            return imageInfo;
        }
        catch (Exception exc) {
            Log.LogWarning(exc, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }

    private async Task<FileInfo> ReadFileContent(IFormFile file, CancellationToken cancellationToken)
    {
        var targetStream = new MemoryStream();
        await using var _ = targetStream.ConfigureAwait(false);
        await file.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
        targetStream.Position = 0;
        return new FileInfo(file.FileName, file.ContentType, file.Length, targetStream.ToArray());
    }

    private sealed record ProcessedFileInfo(FileInfo File, Size? Size);

    private sealed record FileInfo(string FileName, string ContentType, long Length, byte[] Content);
}
