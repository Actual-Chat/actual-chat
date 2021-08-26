using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using RestEase;
using Stl.Fusion.Authentication;

namespace ActualChat.Chat.Client
{
    [BasePath("chat")]
    public interface IChatClientDef
    {
        // Commands
        [Post(nameof(Create))]
        Task<Chat> Create([Body] ChatCommands.Create command, CancellationToken cancellationToken = default);
        [Post(nameof(Post))]
        Task<ChatEntry> Post([Body] ChatCommands.Post command, CancellationToken cancellationToken = default);

        // Queries
        [Get(nameof(TryGet))]
        Task<Chat?> TryGet(Session session, string chatId, CancellationToken cancellationToken = default);

        [Get(nameof(GetEntryCount))]
        Task<long> GetEntryCount(
            Session session, string chatId, Range<long>? idRange,
            CancellationToken cancellationToken = default);
        [Get(nameof(GetPage))]
        Task<ChatPage> GetPage(
            Session session, string chatId, Range<long> idRange,
            CancellationToken cancellationToken = default);
        [Get(nameof(GetLastEntryId))]
        Task<long> GetLastEntryId(
            Session session, string chatId,
            CancellationToken cancellationToken = default);

        [Get(nameof(GetPermissions))]
        Task<ChatPermissions> GetPermissions(
            Session session, string chatId,
            CancellationToken cancellationToken = default);
    }
}
