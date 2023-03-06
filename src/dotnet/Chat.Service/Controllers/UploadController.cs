using ActualChat.Web.Internal;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class UploadController : UploadControllerBase
{
    private readonly IChats _chats;

    public UploadController(IChats chats) => _chats = chats;

    [HttpPost, Route("api/chats/{chatId}/upload-picture")]
    public Task<IActionResult> UploadPicture(ChatId chatId, CancellationToken cancellationToken)
    {
        return Upload(ValidateRequest, GetContentIdPrefix, cancellationToken);

        async ValueTask<IActionResult?> ValidateRequest()
        {
            var chat = await _chats.Get(SessionResolver.Session, chatId, cancellationToken).ConfigureAwait(false);
            return chat == null ? NotFound() : null;
        }

        string GetContentIdPrefix() => $"chat-pictures/{chatId}/picture-";
    }

    [HttpPost, Route("api/chats/upload-picture")]
    public Task<ActionResult<MediaId>> UploadPicture(CancellationToken cancellationToken)
        => Upload("media/chat-pictures", cancellationToken);
}
