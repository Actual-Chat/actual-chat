using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController, JsonifyErrors]
    public class ChatController : ControllerBase, IChatService
    {
        private readonly IChatService _chats;
        private readonly ISessionResolver _sessionResolver;

        public ChatController(IChatService chats, ISessionResolver sessionResolver)
        {
            _chats = chats;
            _sessionResolver = sessionResolver;
        }

        // Commands

        [HttpPost]
        public Task<Chat> CreateChat([FromBody] ChatCommands.CreateChat command, CancellationToken cancellationToken)
        {
            command.UseDefaultSession(_sessionResolver);
            return _chats.CreateChat(command, cancellationToken);
        }

        [HttpPost]
        public Task<ChatEntry> PostMessage([FromBody] ChatCommands.PostMessage command, CancellationToken cancellationToken)
        {
            command.UseDefaultSession(_sessionResolver);
            return _chats.PostMessage(command, cancellationToken);
        }

        // Queries

        [HttpGet, Publish]
        public Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken)
            => _chats.TryGet(session, chatId, cancellationToken);

        [HttpGet, Publish]
        public Task<long> GetEntryCount(
            Session session,
            ChatId chatId,
            Range<long>? idRange,
            CancellationToken cancellationToken)
            => _chats.GetEntryCount(session, chatId, idRange, cancellationToken);

        [HttpGet, Publish]
        public Task<ImmutableArray<ChatEntry>> GetEntries(
            Session session,
            ChatId chatId,
            Range<long> idRange,
            CancellationToken cancellationToken)
            => _chats.GetEntries(session, chatId, idRange, cancellationToken);

        [HttpGet, Publish]
        public Task<Range<long>> GetMinMaxId(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
            => _chats.GetMinMaxId(session, chatId, cancellationToken);

        [HttpGet, Publish]
        public Task<ChatPermissions> GetPermissions(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
            => _chats.GetPermissions(session, chatId, cancellationToken);
    }
}
