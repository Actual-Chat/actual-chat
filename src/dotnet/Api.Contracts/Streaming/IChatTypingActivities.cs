using ActualChat.Comparison;
using ActualChat.Live;

namespace ActualChat.Streaming;

public interface IChatTypingActivities : IComputeService
{
    // Authors currently typing in a chat, ordered by when each started their current typing streak.
    // Consolidated + compared by sequence so an unchanged set isn't pushed to every observer.
    [ComputeMethod(ConsolidationDelay = 0.2, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<AuthorId>> ListTypingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task SetTyping(Session session, ChatId chatId, TypingActivityKind kind, CancellationToken cancellationToken);
}
