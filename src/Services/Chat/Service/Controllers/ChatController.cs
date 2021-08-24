using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Authentication;
using Stl.Fusion.Server;
using Stl.Time;

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
        public Task<Chat> Create(ChatCommands.Create command, CancellationToken cancellationToken = default)
        {
            command.UseDefaultSession(_sessionResolver);
            return _chats.Create(command, cancellationToken);
        }

        [HttpPost]
        public Task<ChatEntry> Post(ChatCommands.Post command, CancellationToken cancellationToken = default)
        {
            command.UseDefaultSession(_sessionResolver);
            return _chats.Post(command, cancellationToken);
        }

        // Queries

        [HttpGet, Publish]
        Task<Chat?> IChatService.TryGet(Session session, string chatId, CancellationToken cancellationToken)
            => _chats.TryGet(session, chatId, cancellationToken);

        [HttpGet, Publish]
        public Task<long> GetEntryCount(
            Session session, string chatId, Range<long>? idRange,
            CancellationToken cancellationToken = default)
            => _chats.GetEntryCount(session, chatId, idRange, cancellationToken);

        [HttpGet, Publish]
        public Task<ChatPage> GetPage(
            Session session, string chatId, Range<long> idRange,
            CancellationToken cancellationToken = default)
            => _chats.GetPage(session, chatId, idRange, cancellationToken);

        [HttpGet, Publish]
        public Task<long> GetLastEntryId(
            Session session, string chatId,
            CancellationToken cancellationToken = default)
            => _chats.GetLastEntryId(session, chatId, cancellationToken);

        [HttpGet, Publish]
        public Task<ChatPermissions> GetPermissions(
            Session session, string chatId,
            CancellationToken cancellationToken = default)
            => _chats.GetPermissions(session, chatId, cancellationToken);
    }
}
