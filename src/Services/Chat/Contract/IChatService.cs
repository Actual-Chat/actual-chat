using System;
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
        Task<ChatEntry> Post(ChatCommands.PostText command, CancellationToken cancellationToken = default);

        // Queries
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Chat?> TryGet(Session session, string chatId, CancellationToken cancellationToken = default);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ImmutableList<ChatEntry>> GetTail(Session session, string chatId, CancellationToken cancellationToken = default);
    }
}
