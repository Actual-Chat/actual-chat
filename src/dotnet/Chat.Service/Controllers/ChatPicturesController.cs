using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class ChatPicturesController : ControllerBase
{
    private readonly ISessionResolver _sessionResolver;
    private readonly IChats _chats;
    private readonly IContentSaver _contentSaver;

    public ChatPicturesController(
        ISessionResolver sessionResolver,
        IChats chats,
        IContentSaver contentSaver)
    {
        _sessionResolver = sessionResolver;
        _chats = chats;
        _contentSaver = contentSaver;
    }

    [HttpPost, Route("api/chats/{chatId}/upload-picture")]
    public async Task<IActionResult> UploadPicture(string chatId, CancellationToken token)
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

        // TODO Does not work
        // var chat = await _chats.Get(_sessionResolver.Session, chatId, token).ConfigureAwait(false);
        // if (chat == null)
        //     return NotFound();

        var stream = file.OpenReadStream();
        await using var _ = stream.ConfigureAwait(false);
        var fileExtension = Path.GetExtension(file.FileName);
        var pictureName = $"picture-{Ulid.NewUlid().ToString()}{fileExtension}";
        var contentId = $"chat-pictures/{chatId}/{pictureName}";

        var content = new Content(contentId, file.ContentType, stream);
        await _contentSaver.SaveContent(content, token).ConfigureAwait(false);

        return Ok(contentId);
    }
}
