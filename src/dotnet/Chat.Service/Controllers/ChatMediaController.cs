using System.Text;
using ActualChat.Hashing;
using ActualChat.Media;
using ActualChat.Security;
using ActualChat.Uploads;
using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

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

        var uploadedFile = new UploadedStreamFile(formFile.FileName,
            formFile.ContentType,
            formFile.Length,
            () => Task.FromResult(formFile.OpenReadStream()));
        using var processedFile = await UploadProcessors.Process(uploadedFile, cancellationToken).ConfigureAwait(false);
        var media = await SaveMedia(chatId, processedFile.File, processedFile.Size, cancellationToken).ConfigureAwait(false);
        if (processedFile.Thumbnail == null)
            return Ok(new MediaContent(media.Id, media.ContentId));

        var thumbnailMedia = await SaveMedia(chatId, processedFile.Thumbnail, processedFile.Size, cancellationToken).ConfigureAwait(false);
        return Ok(new MediaContent(media.Id, media.ContentId, thumbnailMedia.Id, thumbnailMedia.ContentId));
    }

    private async Task<Media.Media> SaveMedia(ChatId chatId, UploadedFile file, Size? size, CancellationToken cancellationToken)
    {
        var mediaId = new MediaId(chatId, Generate.Option);
        var mediaIdHash = mediaId.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
        var media = new Media.Media(mediaId) {
            ContentId = $"media/{mediaIdHash}/{mediaId.LocalId}{Path.GetExtension(file.FileName)}",
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
        };
        var stream = await file.Open().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false)) {
            var content = new Content(media.ContentId, file.ContentType, stream);
            await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);
        }

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media.Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        return media;
    }
}
