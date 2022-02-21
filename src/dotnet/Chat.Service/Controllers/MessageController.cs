using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class MessageController : ControllerBase
{
    private record AttachedFile(int Id, byte[] Content)
    {
        public string? FileName { get; init; }
        public string? ContentType { get; init; }
    }

    private class Attachment
    {
        public int Id { get; set; }
        public string? FileName { get; set; }
        public string? Description { get; set; }
    }

    private class MessagePayload
    {
        public string Text { get; set; } = "";
        public IList<Attachment> Attachments { get; set; } = new List<Attachment>();
    }

    private class MessagePost
    {
        public MessagePayload? Payload { get; set; }
        public List<AttachedFile> Files { get; } = new ();
    }

    private readonly ISessionResolver _sessionResolver;
    private readonly ICommander _commander;

    public MessageController(ISessionResolver sessionResolver, ICommander commander)
    {
        _sessionResolver = sessionResolver;
        _commander = commander;
    }

    [HttpPost]
    [DisableFormValueModelBindingAttribute]
    [Route("api/chats/{chatId}/message")]
    public async Task<IActionResult> PostMessage(string chatId)
    {
        var request = HttpContext.Request;
        //var cancellationToken = HttpContext.RequestAborted;
        // TODO(DM): add entire request and parts size limit check

        // validation of Content-Type
        // 1. first, it must be a form-data request
        // 2. a boundary should be found in the Content-Type
        if (!request.HasFormContentType ||
            !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaTypeHeader) ||
            string.IsNullOrEmpty(mediaTypeHeader.Boundary.Value))
        {
            return new UnsupportedMediaTypeResult();
        }

        var reader = new MultipartReader(mediaTypeHeader.Boundary.Value, request.Body);
        var section = await reader.ReadNextSectionAsync();

        // This sample try to get the first file from request and save it
        // Make changes according to your needs in actual use

        var post = new MessagePost();

        var ok = true;
        while (section != null) {
            var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition,
                out var contentDisposition);

            if (hasContentDispositionHeader && contentDisposition!.DispositionType.Equals("form-data", StringComparison.Ordinal)) {
                var partName = contentDisposition.Name;
                if (partName.Equals("payload_json", StringComparison.Ordinal)) {
                    if (!await HandlePayloadJsonPart(post, section))
                        ok = false;
                }
                else if (TryExtractFileId(partName, out var fileId)) {
                    if (!await HandleFilePart(post, section, contentDisposition, fileId))
                        ok = false;
                }
                else {
                    ok = false; // unrecognized part
                }
            }
            else {
                ok = false;
            }
            if (!ok)
                break;
            section = await reader.ReadNextSectionAsync();
        }

        if (ok)
            ok = ValidatePost(post);
        if (!ok)
            return BadRequest();

        // TODO(DF): add security checks
        // TODO(DF): storing uploads to blob, check on viruses, detect real content type with file signatures

        var command = new IChats.CreateTextEntryCommand(_sessionResolver.Session, chatId, post.Payload!.Text);
        if (post.Files.Count > 0) {
            command.Uploads = post.Files
                .Select(c => new TextEntryAttachmentUpload(c.FileName ?? "", c.ContentType ?? "", c.Content))
                .ToImmutableArray();
        }

        try {
            var chatEntry = await _commander.Call(command, true, CancellationToken.None);
            return Ok();
        }
        catch (Exception e) {
            return BadRequest("Failed to process command");
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

    private async Task<bool> HandleFilePart(
        MessagePost messagePost,
        MultipartSection section,
        ContentDispositionHeaderValue contentDisposition,
        int fileId)
    {
        if (string.IsNullOrEmpty(contentDisposition.FileName.Value))
            return false;

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

        // TODO(DM): use stream/blob instead of byte array for attached file content
        // TODO(DM): add attached files processing: virus scan, content type verification, image size evaluation

        try {
            byte[] content;
            await using (var targetStream = new MemoryStream()) {
                await section.Body.CopyToAsync(targetStream);
                targetStream.Position = 0;
                content = targetStream.ToArray();
            }
            var file = new AttachedFile(fileId, content) {
                ContentType = section.ContentType,
                FileName = contentDisposition.FileName.Value,
            };
            messagePost.Files.Add(file);
            return true;
        }
        catch (Exception e) {
            return false;
        }
    }

    private bool TryExtractFileId(StringSegment partName, out int fileId)
    {
        fileId = -1;
        const string prefix = "files[";
        const string suffix = "]";
        if (!partName.StartsWith(prefix, StringComparison.Ordinal) && !partName.EndsWith(suffix, StringComparison.Ordinal))
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

        var payloadJson = await section.ReadAsStringAsync();
        try {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var payload = JsonSerializer.Deserialize<MessagePayload>(payloadJson, options);
            messagePost.Payload = payload;
            return true;
        }
        catch(Exception e) {
            return false;
        }
    }
}
