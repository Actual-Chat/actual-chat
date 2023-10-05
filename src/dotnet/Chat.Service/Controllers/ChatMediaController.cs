using ActualChat.Media;
using ActualChat.Security;
using ActualChat.Users;
using ActualChat.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController, Route("api/chat-media")]
public sealed class ChatMediaController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IContentSaver ContentSaver { get; } = services.GetRequiredService<IContentSaver>();
    private IReadOnlyCollection<IUploadProcessor> UploadProcessors { get; }
        = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();
    private ICommander Commander { get; } = services.Commander();

    [HttpPost("{chatId}/upload")]
    public async Task<ActionResult<MediaContent>> Upload(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            // NOTE(AY): Header is used by clients, cookie is used by SSB
            var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.GetSessionFromCookie();
            await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            return BadRequest(e.Message);
        }

        var httpRequest = HttpContext.Request;
        if (!httpRequest.HasFormContentType || httpRequest.Form.Files.Count == 0)
            return BadRequest("No file found.");

        if (httpRequest.Form.Files.Count > 1)
            return BadRequest("Too many files.");

        var file = httpRequest.Form.Files[0];
        if (file.Length == 0)
            return BadRequest("File is empty.");

        if (file.Length > Constants.Attachments.FileSizeLimit)
            return BadRequest("File is too big.");

        var fileInfo = await ReadFileContent(file, cancellationToken).ConfigureAwait(false);
        var (processedFile, size) = await ProcessFile(fileInfo, cancellationToken).ConfigureAwait(false);

        var mediaId = new MediaId(chatId, Generate.Option);
        var hashCode = mediaId.Id.ToString().GetSHA256HashCode(HashEncoding.AlphaNumeric);
        var media = new Media.Media(mediaId) {
            ContentId = $"media/{hashCode}/{mediaId.LocalId}{Path.GetExtension(file.FileName)}",
            FileName = fileInfo.FileName,
            Length = fileInfo.Length,
            ContentType = fileInfo.ContentType,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
        };
        using (var stream = new MemoryStream(processedFile.Content)) {
            var content = new Content(media.ContentId, file.ContentType, stream);
            await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);
        }

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media.Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        return Ok(new MediaContent(media.Id, media.ContentId));
    }

    // Private methods

    private Task<ProcessedFileInfo> ProcessFile(FileInfo file, CancellationToken cancellationToken)
    {
        var processor = UploadProcessors.FirstOrDefault(x => x.Supports(file));
        return processor != null
            ? processor.Process(file, cancellationToken)
            : Task.FromResult(new ProcessedFileInfo(file, null));
    }

    private async Task<FileInfo> ReadFileContent(IFormFile file, CancellationToken cancellationToken)
    {
        var targetStream = new MemoryStream();
        await using var _ = targetStream.ConfigureAwait(false);
        await file.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
        targetStream.Position = 0;
        return new FileInfo(file.FileName, file.ContentType, file.Length, targetStream.ToArray());
    }
}
