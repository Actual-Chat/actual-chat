using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Web.Internal;

public abstract class UploadControllerBase : ControllerBase
{
    private ISessionResolver? _sessionResolver;
    private IContentSaver? _contentSaver;

    protected ISessionResolver SessionResolver => _sessionResolver ??= HttpContext.RequestServices.GetRequiredService<ISessionResolver>();
    private IContentSaver ContentSaver => _contentSaver ??= HttpContext.RequestServices.GetRequiredService<IContentSaver>();

    protected async Task<IActionResult> Upload(
        Func<ValueTask<IActionResult?>> requestValidator,
        Func<string> contentIdPrefixFormatter,
        CancellationToken cancellationToken)
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

        var validationResult = await requestValidator.Invoke().ConfigureAwait(false);
        if (validationResult != null)
            return validationResult;

        var stream = file.OpenReadStream();
        await using var _ = stream.ConfigureAwait(false);

        var fileName = $"{Ulid.NewUlid().ToString()}{Path.GetExtension(file.FileName)}";
        var contentId = $"{contentIdPrefixFormatter.Invoke()}{fileName}";

        var content = new Content(contentId, file.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        return Ok(contentId);
    }

    protected async Task<ActionResult<MediaId>> Upload(
        string contentPrefix,
        CancellationToken cancellationToken)
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
        var media = new Medias.Media(mediaId)
        {
            ContentId = $"{contentPrefix}/{mediaId}{Path.GetExtension(file.FileName)}",
            FileName = file.FileName,
            Length = file.Length,
            ContentType = file.ContentType,
        };

        var stream = file.OpenReadStream();
        await using var _ = stream.ConfigureAwait(false);

        var content = new Content(media.ContentId, file.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        return Ok(mediaId);
    }
}
