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
        Task<Chat> Create(ChatCommands.Create command, CancellationToken cancellationToken = default);
        [Post(nameof(Post))]
        Task<ChatEntry> Post(ChatCommands.Post command, CancellationToken cancellationToken = default);

        // Queries
        [Get(nameof(TryGet))]
        Task<Chat?> TryGet(Session session, string chatId, CancellationToken cancellationToken = default);
        [Get(nameof(GetTail))]
        Task<ImmutableList<ChatEntry>> GetTail(Session session, string chatId, CancellationToken cancellationToken = default);
    }
}
