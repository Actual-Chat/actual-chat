using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Chat
{
    public interface IServerSideChatService : IChatService
    {
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Chat?> TryGet(string chatId, CancellationToken cancellationToken = default);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPermissions> GetUserPermissions(
            string chatId, string userId, CancellationToken cancellationToken = default);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<long> GetEntryCount(
            string chatId, TimeRange? timeRange,
            CancellationToken cancellationToken = default);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ImmutableList<ChatEntry>> GetTail(string chatId, CancellationToken cancellationToken = default);
    }
}
