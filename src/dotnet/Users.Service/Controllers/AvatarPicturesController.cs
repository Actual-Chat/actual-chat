using System.Text;
using ActualChat.Controllers;
using ActualChat.Hashing;
using ActualChat.Media;
using ActualChat.Security;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController, Route("api/avatars")]
public sealed class AvatarPicturesController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IContentSaver ContentSaver => services.GetRequiredService<IContentSaver>();
    private ICommander Commander => services.Commander();

    [HttpPost("upload-picture")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(Constants.Attachments.AvatarPictureFileSizeLimit * 2)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.Attachments.AvatarPictureFileSizeLimit * 2)]
    public async Task<ActionResult<MediaContent>> UploadPicture(CancellationToken cancellationToken)
    {
        AccountFull account;
        try {
            // NOTE(AY): Header is used by clients, cookie is used by SSB
            var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.GetSessionFromCookie();
            account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
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
            return BadRequest("Image is empty.");

        if (file.Length > Constants.Attachments.AvatarPictureFileSizeLimit)
            return BadRequest("Image is too big.");

        var mediaId = new MediaId(account.Id, Generate.Option);
        var mediaIdHash = mediaId.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
        var media = new Media.Media(mediaId) {
            ContentId = $"media/{mediaIdHash}/{mediaId.LocalId}{Path.GetExtension(file.FileName)}",
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
        };

        var stream = file.OpenReadStream();
        await using (var _ = stream.ConfigureAwait(false)) {
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
}
