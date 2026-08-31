using ActualChat.Comparison;
using ActualChat.Live;

namespace ActualChat.Streaming;

public interface ITypingIndicators : IComputeService
{
    // Authors currently typing in a chat, ordered by when each started their current typing streak -
    // the UI shows the first one and moves to the next when they stop. Consolidated + compared by
    // sequence so an unchanged set isn't pushed to every observer.
    [ComputeMethod(ConsolidationDelay = 0.2, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ApiArray<AuthorId>> ListTypingAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    Task SetTyping(Session session, ChatId chatId, TypingKind kind, bool isActive, CancellationToken cancellationToken);
}
