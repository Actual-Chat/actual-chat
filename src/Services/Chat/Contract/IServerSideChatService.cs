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
        Task<Chat?> TryGet(ChatId chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<long> GetEntryCount(
            ChatId chatId, 
            Range<long>? idRange,
            CancellationToken cancellationToken);
        
        [ComputeMethod(KeepAliveTime = 1)]
        Task<ImmutableArray<ChatEntry>> GetPage(
            ChatId chatId,
            Range<long> idRange,
            CancellationToken cancellationToken);
        
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPermissions> GetPermissions(ChatId chatId, UserId userId, CancellationToken cancellationToken);
    }
}
