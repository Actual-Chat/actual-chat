using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Web.Internal;

public abstract class UploadControllerBase : ControllerBase
{
    private ISessionResolver? _sessionResolver;
    private IContentSaver? _contentSaver;

    protected ISessionResolver SessionResolver => _sessionResolver ??= HttpContext.RequestServices.GetRequiredService<ISessionResolver>();
    private IContentSaver ContentSaver => _contentSaver ??= HttpContext.RequestServices.GetRequiredService<IContentSaver>();

    protected async Task<IActionResult> Upload(Func<Task<IActionResult?>> validateRequest, Func<string> getContentIdPrefix, CancellationToken cancellationToken)
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

        var validationResult = await validateRequest().ConfigureAwait(false);
        if (validationResult != null)
            return validationResult;

        var stream = file.OpenReadStream();
        await using var _ = stream.ConfigureAwait(false);
        var fileExtension = Path.GetExtension(file.FileName);
        var contentName = $"{Ulid.NewUlid().ToString()}{fileExtension}";
        var contentId = $"{getContentIdPrefix()}{contentName}";

        var content = new Content(contentId, file.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        return Ok(contentId);
    }
}
