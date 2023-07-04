using ActualChat.Media;
using ActualChat.Web;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController]
public sealed class AvatarPicturesController : ControllerBase
{
    private IContentSaver ContentSaver { get; }
    private ICommander Commander { get; }
    private SessionCookies SessionCookies { get; }
    private ISessionResolver SessionResolver { get; }
    private IAuth Auth { get; }

    public AvatarPicturesController(
        IContentSaver contentSaver,
        ICommander commander,
        SessionCookies sessionCookies,
        ISessionResolver sessionResolver,
        IAuth auth)
    {
        ContentSaver = contentSaver;
        Commander = commander;
        SessionCookies = sessionCookies;
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

        var session = SessionCookies.Read(HttpContext);
        if (session is null)
            return Forbid();

        SessionResolver.Session = session;
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Forbid();

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
