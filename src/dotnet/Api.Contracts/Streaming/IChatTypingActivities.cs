using ActualChat.Comparison;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public interface IChatTypingActivities : IComputeService
{
    // Authors currently typing in a chat, ordered by when each started their current typing streak.
    // Consolidated + compared by sequence so an unchanged set isn't pushed to every observer.
    [ComputeMethod(ConsolidationDelay = 0.2, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<AuthorId>> ListTypingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    // ttl is how much longer the author is to be shown as typing - the client renews this lease
    // while it types, and the server clamps ttl to Constants.Typing.MaxTtl. Nothing here is worth
    // waiting or retrying for: a renewal that lands late would revive an author who already
    // stopped, so the call fails fast instead of being resent over a new connection.
    [RpcMethod(
        RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection,
        ConnectTimeout = 1,
        RunTimeout = Constants.Typing.MaxTtlSeconds)]
    Task SetTyping(
        Session session,
        ChatId chatId,
        TypingActivityKind kind,
        TimeSpan ttl,
        CancellationToken cancellationToken);
}
