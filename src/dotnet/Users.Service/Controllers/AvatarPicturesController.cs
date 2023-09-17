using ActualChat.Media;
using ActualChat.Security;
using ActualChat.Web;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController, Route("api/avatars")]
public sealed class AvatarPicturesController(IServiceProvider services) : ControllerBase
{
    private IAuth? _auth;
    private IContentSaver? _contentSaver;
    private ICommander? _commander;

    private IServiceProvider Services { get; } = services;
    private IAuth Auth => _auth ??= Services.GetRequiredService<IAuth>();
    private IContentSaver ContentSaver => _contentSaver ??= Services.GetRequiredService<IContentSaver>();
    private ICommander Commander => _commander ??= Services.Commander();

    [HttpPost("upload-picture")]
    public async Task<ActionResult<MediaContent>> UploadPicture(CancellationToken cancellationToken)
    {
        // NOTE(AY): Header is used by clients, cookie is used by SSB
        var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token) ?? HttpContext.GetSessionFromCookie();

        var httpRequest = HttpContext.Request;
        if (!httpRequest.HasFormContentType || httpRequest.Form.Files.Count == 0)
            return BadRequest("No file content found");

        if (httpRequest.Form.Files.Count > 1)
            return BadRequest("Too many files");

        var file = httpRequest.Form.Files[0];
        if (file.Length == 0)
            return BadRequest("Image is empty");

        if (file.Length > Constants.Attachments.FileSizeLimit)
            return BadRequest("Image is too big");

        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return BadRequest("No Account");

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
