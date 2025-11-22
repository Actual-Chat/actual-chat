using ActualChat.Controllers;
using ActualChat.Security;
using ActualChat.Uploads;
using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController, Route("api/chat-media")]
public sealed class ChatMediaController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaSaver MediaSaver { get; } = services.GetRequiredService<IMediaSaver>();
    private IReadOnlyCollection<IUploadProcessor> UploadProcessors { get; }
        = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();

    [HttpPost("{chatId}/upload")]
    [DisableFormValueModelBinding]
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

        var file = httpRequest.Form.Files[0];
        if (file.Length == 0)
            return BadRequest("File is empty.");

        if (file.Length > Constants.Attachments.FileSizeLimit)
            return BadRequest("File is too big.");

        var uploadedFile = new UploadedStreamFile(
            file.FileName,
            file.ContentType,
            file.Length,
            () => Task.FromResult(file.OpenReadStream()));
        using var processedFile = await UploadProcessors.Process(uploadedFile, cancellationToken).ConfigureAwait(false);
        var media = await MediaSaver.Save(chatId, processedFile.File, processedFile.Size, cancellationToken)
            .ConfigureAwait(false);
        if (processedFile.Thumbnail == null)
            return Ok(new MediaContent(media.Id, media.ContentId));

        var thumbnailMedia = await MediaSaver
            .Save(chatId, processedFile.Thumbnail, processedFile.Size, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new MediaContent(media.Id, media.ContentId, thumbnailMedia.Id, thumbnailMedia.ContentId));
    }
}
