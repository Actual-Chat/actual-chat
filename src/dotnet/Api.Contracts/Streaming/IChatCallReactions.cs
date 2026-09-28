using ActualChat.Comparison;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IChatCallReactions : IComputeService
{
    // Emoji reactions sent in the chat's call over the last Constants.Call.ReactionDuration,
    // at most one per author - a newer one replaces it.
    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(ApiArrayComparer<CallReaction>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<CallReaction>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    // A reaction is only worth showing right away, so a late one is dropped rather than resent.
    [RpcMethod(
        RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection,
        ConnectTimeout = 1,
        RunTimeout = 5)]
    Task Send(Session session, ChatId chatId, Emoji emoji, CancellationToken cancellationToken);
}
