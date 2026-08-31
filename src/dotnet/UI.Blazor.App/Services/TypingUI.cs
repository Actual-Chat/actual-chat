using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class TypingUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly TimeSpan RotationPeriod = TimeSpan.FromSeconds(2);

    private IChatTypingActivities ChatTypingActivities => Hub.ChatTypingActivities;
    private IAuthors Authors => Hub.Authors;
    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;

    [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // Everyone but me, in the server's "who started first" order.
        // While the RPC peer is down we stop receiving invalidations, so the last known value is stale.
        var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken).ConfigureAwait(false);
        if (!isConnected)
            return default;

        var authorIds = await ChatTypingActivities
            .ListTypingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
        if (authorIds.Count == 0)
            return authorIds;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor is null
            ? authorIds
            : authorIds.Without(id => id == ownAuthor.Id);
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<AuthorId?> GetTypingAuthorId(ChatId chatId, CancellationToken cancellationToken)
    {
        // The single typist to show. Several of them take turns, one RotationPeriod each,
        // so nobody stays hidden behind the one who started first.
        var computed = Computed.GetCurrent();
        var authorIds = await ListTypingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Count == 0)
            return null;
        if (authorIds.Count == 1)
            return authorIds[0];

        var now = Clocks.SystemClock.Now;
        var slot = now.EpochOffset.Ticks / RotationPeriod.Ticks;
        var slotEndsAt = new Moment(TimeSpan.FromTicks((slot + 1) * RotationPeriod.Ticks));
        computed.Invalidate(slotEndsAt - now);
        return authorIds[(int)(slot % authorIds.Count)];
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsTyping(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        // Per-member typing state for the right-panel Members list.
        var authorIds = await ListTypingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Contains(authorId);
    }

    public Task SetTyping(ChatId chatId, TypingActivityKind kind, CancellationToken cancellationToken)
        => ChatTypingActivities.SetTyping(Session, chatId, kind, cancellationToken);
}
