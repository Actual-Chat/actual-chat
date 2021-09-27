using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Stl.CommandR.Configuration;
using Stl.Fusion;
using Stl.Fusion.Authentication;

namespace ActualChat.Chat
{
    public interface IChatService
    {
        // Commands
        [CommandHandler]
        Task<Chat> Create(ChatCommands.Create command, CancellationToken cancellationToken);
        [CommandHandler]
        Task<ChatEntry> Post(ChatCommands.Post command, CancellationToken cancellationToken);

        // Queries
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<long> GetEntryCount(
            Session session, ChatId chatId, Range<long>? idRange,
            CancellationToken cancellationToken);
        [ComputeMethod(KeepAliveTime = 1)]
        Task<ImmutableArray<ChatEntry>> GetEntries(
            Session session, ChatId chatId, Range<long> idRange,
            CancellationToken cancellationToken);
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Range<long>> GetIdRange(
            Session session, ChatId chatId,
            CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPermissions> GetPermissions(
            Session session, ChatId chatId,
            CancellationToken cancellationToken);
    }
}
