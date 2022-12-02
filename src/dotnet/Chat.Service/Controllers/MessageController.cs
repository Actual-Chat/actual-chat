using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class MessageController : ControllerBase
{
    private readonly ISessionResolver _sessionResolver;
    private readonly ICommander _commander;
    private readonly ILogger<MessageController> _log;

    public MessageController(ISessionResolver sessionResolver, ICommander commander, ILogger<MessageController> log)
    {
        _sessionResolver = sessionResolver;
        _commander = commander;
        _log = log;
    }

    [HttpPost]
    [DisableFormValueModelBinding]
    [Route("api/chats/{chatId}/message")]
    public async Task<ActionResult<long>> PostMessage(ChatId chatId)
    {
        var request = HttpContext.Request;
        //var cancellationToken = HttpContext.RequestAborted;
        // TODO(DF): add entire request and parts size limit check

        // validation of Content-Type
        // 1. first, it must be a form-data request
        // 2. a boundary should be found in the Content-Type
        if (!request.HasFormContentType ||
            !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaTypeHeader) ||
            mediaTypeHeader.Boundary.Value.IsNullOrEmpty())
        {
            return new UnsupportedMediaTypeResult();
        }

        var reader = new MultipartReader(mediaTypeHeader.Boundary.Value, request.Body);
        var section = await reader.ReadNextSectionAsync().ConfigureAwait(false);

        // This sample try to get the first file from request and save it
        // Make changes according to your needs in actual use

        var post = new MessagePost();

        var incorrectPart = false;
        while (section != null) {
            var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition,
                out var contentDisposition);

            if (hasContentDispositionHeader && OrdinalEquals(contentDisposition!.DispositionType, "form-data")) {
                var partName = contentDisposition.Name;
                // NOTE(AY): Same here
                if (OrdinalEquals(partName, "payload_json")) {
                    if (!await HandlePayloadJsonPart(post, section).ConfigureAwait(false))
                        incorrectPart = true;
                }
                else if (TryExtractFileId(partName, out var fileId)) {
                    var file = await HandleFilePart(section, contentDisposition, fileId).ConfigureAwait(false);
                    if (file == null)
                        incorrectPart = true;
                    else if (file.Content.Length > Constants.Attachments.FileSizeLimit)
                        incorrectPart = true;
                    else if (post.Files.Count >= Constants.Attachments.FileCountLimit)
                        incorrectPart = true;
                    else
                        post.Files.Add(file);
                }
                else {
                    incorrectPart = true; // unrecognized part
                }
            }
            else {
                incorrectPart = true;
            }
            if (incorrectPart)
                break;
            section = await reader.ReadNextSectionAsync().ConfigureAwait(false);
        }

        if (incorrectPart)
            return BadRequest("Incorrect part");
        if (!ValidatePost(post))
            return BadRequest("Required part is missing");

        // TODO(DF): add security checks
        // TODO(DF): storing uploads to blob, check on viruses, detect real content type with file signatures

        var command = new IChats.UpsertTextEntryCommand(
            _sessionResolver.Session,
            chatId,
            null,
            post.Payload!.Text.Trim(),
            post.Payload!.RepliedChatEntryId
        );
        if (post.Files.Count > 0) {
            var uploads = new List<TextEntryAttachmentUpload>();
            foreach (var file in post.Files) {
                var attributes = post.Payload.Attachments.First(c => c.Id == file.Id);
                var fileName = attributes.FileName.IsNullOrEmpty() ? file.FileName : attributes.FileName;
                var description = attributes.Description ?? "";
                var (processedFile, imageSize) = await ProcessFile(file).ConfigureAwait(false);
                var upload = new TextEntryAttachmentUpload(fileName, processedFile.Content, processedFile.ContentType) {
                    Description = description,
                    Width = imageSize?.Width ?? 0,
                    Height = imageSize?.Height ?? 0,
                };
                uploads.Add(upload);
            }
            command.Attachments = uploads.ToImmutableArray();
        }

        try {
            var chatEntry = await _commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
            return chatEntry.LocalId;
        }
        catch {
            return BadRequest("Failed to process command");
        }
    }

    private async Task<ProcessedFileInfo> ProcessFile(FileInfo file)
    {
        if (!file.ContentType.OrdinalIgnoreCaseContains("image"))
            return new ProcessedFileInfo(file, null);

        var imageInfo = await GetImageInfo(file).ConfigureAwait(false);
        if (imageInfo == null)
            return new ProcessedFileInfo(file with { ContentType = System.Net.Mime.MediaTypeNames.Application.Octet }, null);

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
                    img.Resize(new ResizeOptions {Mode = ResizeMode.Max, Size = new Size(sizeLimit)});
            });
            image.Metadata.ExifProfile = null;
            imageSize = image.Size();
            await image.SaveAsync(targetStream, imageFormat).ConfigureAwait(false);
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
            _log.LogWarning(exc, "Failed to extract image info from '{FileName}'", file.FileName);
            return null;
        }
    }

    private bool ValidatePost(MessagePost post)
    {
        if (post.Payload == null)
            return false;
        if (post.Payload.Attachments.Count != post.Files.Count)
            return false;

        foreach (var attachment in post.Payload.Attachments) {
            var file = post.Files.FirstOrDefault(c => c.Id == attachment.Id);
            if (file == null)
                return false;
        }
        return true;
    }

    private async Task<FileInfo?> HandleFilePart(
        MultipartSection section,
        ContentDispositionHeaderValue contentDisposition,
        int fileId)
    {
        if (contentDisposition.FileName.Value.IsNullOrEmpty())
            return null;

        // Don't trust any file name, file extension, and file data from the request unless you trust them completely
        // Otherwise, it is very likely to cause problems such as virus uploading, disk filling, etc
        // In short, it is necessary to restrict and verify the upload
        // Here, we just use the temporary folder and a random file name

        // Get the temporary folder, and combine a random file name with it
        // var fileName = Path.GetRandomFileName();
        // var saveToPath = Path.Combine(Path.GetTempPath(), fileName);
        //
        // using (var targetStream = System.IO.File.Create(saveToPath))
        // {
        //     await section.Body.CopyToAsync(targetStream);
        // }

        // TODO(DF): use stream/blob instead of byte array for attached file content
        // TODO(DF): add attached files processing: virus scan, content type verification, image size evaluation

        try {
            byte[] content;
            var targetStream = new MemoryStream();
            await using (var _ = targetStream.ConfigureAwait(false)) {
                await section.Body.CopyToAsync(targetStream).ConfigureAwait(false);
                targetStream.Position = 0;
                content = targetStream.ToArray();
            }
            var file = new FileInfo(fileId, content) {
                ContentType = section.ContentType ?? "",
                FileName = contentDisposition.FileName.Value ?? "",
            };
            return file;
        }
        catch {
            return null;
        }
    }

    private bool TryExtractFileId(StringSegment partName, out int fileId)
    {
        fileId = -1;
        const string prefix = "files[";
        const string suffix = "]";
        if (!partName.OrdinalStartsWith(prefix) && !partName.OrdinalEndsWith(suffix))
            return false;
        var idSegment = partName.Subsegment(prefix.Length, partName.Length - prefix.Length - suffix.Length);
        if (!int.TryParse(idSegment.AsSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tempFileId))
            return false;
        fileId = tempFileId;
        return true;
    }

    private async Task<bool> HandlePayloadJsonPart(
        MessagePost messagePost,
        MultipartSection section)
    {
        if (messagePost.Payload != null)
            return false;

        var payloadJson = await section.ReadAsStringAsync().ConfigureAwait(false);
        try {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
#pragma warning disable IL2026
            var payload = JsonSerializer.Deserialize<MessagePayload>(payloadJson, options);
#pragma warning restore IL2026
            messagePost.Payload = payload;
            return true;
        }
        catch (Exception exc) {
            _log.LogDebug(exc, "Failed to deserialize message payload");
            return false;
        }
    }

    // Nested types

    private sealed record ProcessedFileInfo(FileInfo File, Size? Size);

    private sealed record FileInfo(int Id, byte[] Content)
    {
        public string FileName { get; init; } = "";
        public string ContentType { get; init; } = "";
    }

    private sealed class FileAttributes
    {
        public int Id { get; init; }
        public string FileName { get; init; } = "";
        public string Description { get; init; } = "";
    }

    private sealed class MessagePayload
    {
        public string Text { get; init; } = "";
        public IList<FileAttributes> Attachments { get; init; } = new List<FileAttributes>();
        public long? RepliedChatEntryId { get; init; }
    }

    private sealed class MessagePost
    {
        public MessagePayload? Payload { get; set; }
        public List<FileInfo> Files { get; set; } = new();
    }
}
