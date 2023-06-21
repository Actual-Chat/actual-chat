using ActualChat.Media;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController]
public sealed class AvatarPicturesController : ControllerBase
{
    private IContentSaver ContentSaver { get; }
    private ICommander Commander { get; }
    private ISessionResolver SessionResolver { get; }
    private IAuth Auth { get; }

    public AvatarPicturesController(
        IContentSaver contentSaver,
        ICommander commander,
        ISessionResolver sessionResolver,
        IAuth auth)
    {
        ContentSaver = contentSaver;
        Commander = commander;
        SessionResolver = sessionResolver;
        Auth = auth;
    }

    [HttpPost, Route("api/avatars/upload-picture")]
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

        if (file.Length > Constants.Chat.FileSizeLimit)
            return BadRequest("Image is too big");

        var user = await Auth.GetUser(SessionResolver.Session, cancellationToken).ConfigureAwait(false);
        var mediaId = new MediaId(user!.Id, Generate.Option);
        var hashCode = mediaId.Id.ToString().GetSHA256HashCode();
        var media = new Media.Media(mediaId) {
            ContentId = $"media/{hashCode}/{mediaId.LocalId}{Path.GetExtension(file.FileName)}",
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
        };

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media.Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        var stream = file.OpenReadStream();
        await using var _ = stream.ConfigureAwait(false);

        var content = new Content(media.ContentId, file.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        return Ok(new MediaContent(media.Id, media.ContentId));
    }
}
