using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Stl.CommandR.Configuration;
using Stl.Fusion;

namespace ActualChat.Chat
{
    public interface IServerSideChatService : IChatService
    {
        // Commands
        [CommandHandler]
        Task<ChatEntry> CreateEntry(ChatCommands.CreateEntry command, CancellationToken cancellationToken);

        // Queries
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Chat?> TryGet(string chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<long> GetEntryCount(
            string chatId, Range<long>? idRange,
            CancellationToken cancellationToken);
        [ComputeMethod(KeepAliveTime = 1)]
        Task<ImmutableArray<ChatEntry>> GetPage(
            string chatId, Range<long> idRange,
            CancellationToken cancellationToken);
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Range<long>> GetIdRange(string chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPermissions> GetPermissions(
            string chatId, string userId, CancellationToken cancellationToken);
    }
}
