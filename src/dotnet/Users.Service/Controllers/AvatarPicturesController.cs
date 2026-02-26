using ActualChat.AspNetCore;
using ActualChat.Controllers;
using ActualChat.Media;
using ActualChat.Security;
using ActualChat.Uploads;
using ActualChat.Users.AvatarIcons;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController, Route("api/avatars")]
public sealed class AvatarPicturesController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaSaver MediaSaver => services.GetRequiredService<IMediaSaver>();

    [HttpPost("upload-picture")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(Constants.Attachments.AvatarPictureFileSizeLimit * 2)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.Attachments.AvatarPictureFileSizeLimit * 2)]
    public async Task<ActionResult<MediaRef>> UploadPicture(CancellationToken cancellationToken)
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

        var mediaId = MediaId.New(account.Id.Value);
        var uploadedFile = new UploadedStreamFile(
            file.FileName,
            file.ContentType,
            file.Length,
            () => Task.FromResult(file.OpenReadStream()));
        var mediaRef = await MediaSaver
            .Save(mediaId, uploadedFile, null, MediaKind.UserAvatarPicture, cancellationToken)
            .ConfigureAwait(false);
        return Ok(mediaRef);
    }

    [HttpGet("beam/{key}")]
    [CacheControlImmutable(Duration = 2592000)] // 30 days
    public ActionResult GetBeam(BeamAvatarQuery query)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (query.Format == AvatarFormat.Png) {
            var pngSize = query.Size ?? 80;
            var pngBytes = BeamAvatars.GeneratePngBytes(query.Key, pngSize, square: false);
            return File(pngBytes, "image/png");
        }

        var svg = BeamAvatars.GenerateSvg(query.Key, square: false);
        return Content(svg, "image/svg+xml");
    }

    [HttpGet("marble/{key}")]
    [CacheControlImmutable(Duration = 2592000)] // 30 days
    public ActionResult GetMarble(MarbleAvatarQuery query)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (query.Format == AvatarFormat.Png) {
            var pngSize = query.Size ?? 80;
            var pngBytes = MarbleAvatars.GeneratePngBytes(query.Key, pngSize, title: query.Title ?? "", doNotBlur: query.DoNotBlur);
            return File(pngBytes, "image/png");
        }

        var svg = MarbleAvatars.GenerateSvg(query.Key, title: query.Title ?? "", doNotBlur: query.DoNotBlur);
        return Content(svg, "image/svg+xml");
    }
}
