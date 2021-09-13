using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;
using Stl.Time;

namespace ActualChat.Chat
{
    public interface IServerSideChatService : IChatService
    {
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Chat?> TryGet(string chatId, CancellationToken cancellationToken = default);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<long> GetEntryCount(
            string chatId, Range<long>? idRange,
            CancellationToken cancellationToken = default);
        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPage> GetPage(
            string chatId, Range<long> idRange,
            CancellationToken cancellationToken = default);
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Range<long>> GetIdRange(string chatId, CancellationToken cancellationToken = default);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPermissions> GetPermissions(
            string chatId, string userId, CancellationToken cancellationToken = default);
    }
}
