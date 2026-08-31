using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class TypingUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ITypingIndicators TypingIndicators => Hub.TypingIndicators;
    private IAuthors Authors => Hub.Authors;
    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;

    // Authors typing in the chat other than me, ordered by who started first (server order kept).
    [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        // While the RPC peer is down we stop receiving invalidations, so the last known value is stale.
        var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken).ConfigureAwait(false);
        if (!isConnected)
            return default;

        var authorIds = await TypingIndicators.ListTypingAuthorIds(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Count == 0)
            return authorIds;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor is null
            ? authorIds
            : authorIds.Where(id => id != ownAuthor.Id).ToApiArray();
    }

    // The single typist to show - the earliest still typing, or null when nobody is.
    [ComputeMethod]
    public virtual async Task<AuthorId?> GetTypingAuthorId(ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await ListTypingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Count > 0 ? authorIds[0] : null;
    }

    // Per-member typing state for the right-panel Members list.
    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsTyping(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var authorIds = await ListTypingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Contains(authorId);
    }

    public Task SetTyping(ChatId chatId, TypingKind kind, bool isActive, CancellationToken cancellationToken)
        => TypingIndicators.SetTyping(Session, chatId, kind, isActive, cancellationToken);
}
