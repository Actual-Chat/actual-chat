using ActualChat.Media;
using ActualChat.Security;
using ActualChat.Uploads;
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
    [RequestSizeLimit(Constants.Attachments.FileSizeLimit * 2)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.Attachments.FileSizeLimit * 2)]
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

        var formFile = httpRequest.Form.Files[0];
        if (formFile.Length == 0)
            return BadRequest("File is empty.");

        if (formFile.Length > Constants.Attachments.FileSizeLimit)
            return BadRequest("File is too big.");

        using var processedFile = await Process(cancellationToken, formFile).ConfigureAwait(false);
        var mediaId = new MediaId(chatId, Generate.Option);
        var hashCode = mediaId.Id.ToString().GetSHA256HashCode(HashEncoding.AlphaNumeric);
        var media = new Media.Media(mediaId) {
            ContentId = $"media/{hashCode}/{mediaId.LocalId}{Path.GetExtension(formFile.FileName)}",
            FileName = processedFile.File.FileName,
            Length = processedFile.File.Length,
            ContentType = processedFile.File.ContentType,
            Width = processedFile.Size?.Width ?? 0,
            Height = processedFile.Size?.Height ?? 0,
        };
        var stream = processedFile.File.Open();
        await using (stream.ConfigureAwait(false)) {
            var content = new Content(media.ContentId, formFile.ContentType, stream);
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

    private async Task<ProcessedFile> Process(CancellationToken cancellationToken, IFormFile formFile)
    {
        var uploadedFile = await ReadFileContent(formFile, cancellationToken).ConfigureAwait(false);
        var processor = UploadProcessors.FirstOrDefault(x => x.Supports(uploadedFile));
        if (processor == null)
            return new ProcessedFile(uploadedFile, null);

        var processedFile = await processor.Process(uploadedFile, cancellationToken).ConfigureAwait(false);
        if (processedFile.File.TempFilePath != uploadedFile.TempFilePath)
            uploadedFile.Delete();

        return processedFile;
    }

    // Private methods

    private async Task<UploadedFile> ReadFileContent(IFormFile file, CancellationToken cancellationToken)
    {
        var fileName = Path.ChangeExtension(file.FileName, $"_{Guid.NewGuid()}" + Path.GetExtension(file.FileName));
        var targetFilePath = Path.Combine(Path.GetTempPath(), fileName);
        var target = System.IO.File.OpenWrite(targetFilePath);
        await using var _ = target.ConfigureAwait(false);
        await file.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedFile(file.FileName, file.ContentType, file.Length, targetFilePath);
    }
}
