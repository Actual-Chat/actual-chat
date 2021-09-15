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
        Task<Chat> Create([Body] ChatCommands.Create command, CancellationToken cancellationToken);
        [Post(nameof(Post))]
        Task<ChatEntry> Post([Body] ChatCommands.Post command, CancellationToken cancellationToken);

        // Queries
        [Get(nameof(TryGet))]
        Task<Chat?> TryGet(Session session, string chatId, CancellationToken cancellationToken);

        [Get(nameof(GetEntryCount))]
        Task<long> GetEntryCount(
            Session session, string chatId, Range<long>? idRange,
            CancellationToken cancellationToken);
        [Get(nameof(GetEntries))]
        Task<ImmutableArray<ChatEntry>> GetEntries(
            Session session, string chatId, Range<long> idRange,
            CancellationToken cancellationToken);
        [Get(nameof(GetIdRange))]
        Task<Range<long>> GetIdRange(
            Session session, string chatId,
            CancellationToken cancellationToken);

        [Get(nameof(GetPermissions))]
        Task<ChatPermissions> GetPermissions(
            Session session, string chatId,
            CancellationToken cancellationToken);
    }
}
