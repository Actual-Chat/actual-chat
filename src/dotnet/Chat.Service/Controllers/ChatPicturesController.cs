using ActualChat.Web.Internal;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController]
public class ChatPicturesController : UploadControllerBase
{
    private readonly IChats _chats;

    public ChatPicturesController(IChats chats) => _chats = chats;

    [HttpPost, Route("api/chats/{chatId}/upload-picture")]
    public Task<IActionResult> UploadPicture(string chatId, CancellationToken cancellationToken)
    {
        return Upload(ValidateRequest, GetContentIdPrefix, cancellationToken);

        async Task<IActionResult?> ValidateRequest()
        {
            var chat = await _chats.Get(SessionResolver.Session, chatId, cancellationToken).ConfigureAwait(false);
            return chat == null ? NotFound() : null;
        }

        string GetContentIdPrefix() => $"chat-pictures/{chatId}/picture-";
    }
}
