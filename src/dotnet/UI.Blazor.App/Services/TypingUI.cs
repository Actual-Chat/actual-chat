using ActualChat.Comparison;
using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public class TypingUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly TimeSpan RotationPeriod = TimeSpan.FromSeconds(2);

    private IChatTypingActivities ChatTypingActivities => Hub.ChatTypingActivities;
    private ChatActivityUI ChatActivityUI => Hub.ChatActivityUI;

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

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // Everyone but me, in the server's "who started first" order. Consolidated, as the summary
        // carrying it also changes with the chat's call activity.
        var summary = await ChatActivityUI.GetSummary(chatId, cancellationToken).ConfigureAwait(false);
        return summary.TypingAuthorIds;
    }

    public Task SetTyping(
        ChatId chatId,
        TypingActivityKind kind,
        TimeSpan ttl,
        CancellationToken cancellationToken)
        => ChatTypingActivities.SetTyping(Session, chatId, kind, ttl, cancellationToken);
}
