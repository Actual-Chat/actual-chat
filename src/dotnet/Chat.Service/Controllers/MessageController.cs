﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class MessageController : ControllerBase
{
    private readonly ISessionResolver _sessionResolver;
    private readonly ICommander _commander;

    public MessageController(ISessionResolver sessionResolver, ICommander commander)
    {
        _sessionResolver = sessionResolver;
        _commander = commander;
    }

    [HttpPost]
    [DisableFormValueModelBinding]
    [Route("api/chats/{chatId}/message")]
    public async Task<ActionResult<long>> PostMessage(string chatId)
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

        var command = new IChats.CreateTextEntryCommand(_sessionResolver.Session, chatId, post.Payload!.Text);
        if (post.Files.Count > 0) {
            var uploads = new List<TextEntryAttachmentUpload>();
            foreach (var file in post.Files) {
                var attributes = post.Payload.Attachments.First(c => c.Id == file.Id);
                var fileName = attributes.FileName.IsNullOrEmpty() ? file.FileName : attributes.FileName;
                var description = attributes.Description ?? "";
                var upload = new TextEntryAttachmentUpload(fileName, file.Content, file.ContentType) {
                    Description = description
                };
                uploads.Add(upload);
            }
            command.Attachments = uploads.ToImmutableArray();
        }

        try {
            var chatEntry = await _commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
            return chatEntry.Id;
        }
        catch {
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
        catch {
            return false;
        }
    }

    // Nested types

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
    }

    private sealed class MessagePost
    {
        public MessagePayload? Payload { get; set; }
        public List<FileInfo> Files { get; set; } = new();
    }
}
