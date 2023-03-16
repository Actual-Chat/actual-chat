using ActualChat.Media;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController]
public class AvatarPicturesController : ControllerBase
{
    private IContentSaver ContentSaver { get; }
    private ICommander Commander { get; }

    public AvatarPicturesController(
        IContentSaver contentSaver,
        ICommander commander)
    {
        ContentSaver = contentSaver;
        Commander = commander;
    }

    [HttpPost, Route("api/user-avatars/upload-picture")]
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

        if (file.Length > Constants.Chat.PictureFileSizeLimit)
            return BadRequest("Image is too big");

        var mediaId = new MediaId(Ulid.NewUlid().ToString());
        var media = new Media.Media(mediaId) {
            ContentId = $"media/avatars/{mediaId}{Path.GetExtension(file.FileName)}",
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
        };

        var changeCommand = new IMediaBackend.ChangeCommand(
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
