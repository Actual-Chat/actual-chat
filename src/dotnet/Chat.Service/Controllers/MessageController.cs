using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController]
public sealed class MessageController : ControllerBase
{
    private readonly ISessionResolver _sessionResolver;
    private readonly ICommander _commander;

    public MessageController(ISessionResolver sessionResolver, ICommander commander)
    {
        _sessionResolver = sessionResolver;
        _commander = commander;
    }

    [HttpPost]
    [Route("api/chats/{chatId}/message")]
    public async Task<ActionResult<long>> PostMessage(ChatId chatId, [FromBody] ChatMessage chatMessage)
    {
        // TODO(DF): add security checks
        // TODO(DF): storing uploads to blob, check on viruses, detect real content type with file signatures

        var command = new IChats.UpsertTextEntryCommand(
            _sessionResolver.Session,
            chatId,
            null,
            chatMessage.Text.Trim(),
            chatMessage.RepliedChatEntryId
        );

        if (chatMessage.Attachments.Count > 0)
            command.Attachments = chatMessage.Attachments.ToImmutableArray();

        try {
            var chatEntry = await _commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
            return chatEntry.LocalId;
        }
        catch {
            return BadRequest("Failed to process command");
        }
    }

    // Nested types

    public sealed class ChatMessage
    {
        public string Text { get; init; } = "";
        public IList<MediaId> Attachments { get; init; } = new List<MediaId>();
        public long? RepliedChatEntryId { get; init; }
    }
}
