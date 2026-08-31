using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// Tracks who is typing in a chat. The state is node-local RAM only - it lives in the
/// <see cref="ListRaw"/> computed - so a shard handover simply starts the new owner off empty.
/// </summary>
public partial class ChatTypingActivitiesBackend : ShardComputeService, IChatTypingActivitiesBackend
{
    // Typing is high-churn and short-lived: the client re-emits while typing, and a streak lapses
    // on its own shortly after the last keystroke - no explicit stop needed.
    private static readonly TimeSpan ActivityTtl = TimeSpan.FromSeconds(6);

    private readonly LockingComputeMethodPrimer<ChatId, ApiArray<TypingActivity>> _listRawPrimer;

    public ChatTypingActivitiesBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
        => _listRawPrimer = new LockingComputeMethodPrimer<ChatId, ApiArray<TypingActivity>>(ListRaw);

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // Captured before the awaits below - see LiveSessionsBackend.GetState.
        var computed = Computed.GetCurrent();
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var activities = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        var fresh = activities.Where(x => x.ExpiresAt > now).ToApiArray();
        if (fresh.Count == 0)
            return default;

        computed.InvalidateSafely(fresh.Min(x => x.ExpiresAt) - now, ActivityTtl);
        return fresh
            .OrderBy(x => x.StartedAt)
            .Select(x => x.AuthorId)
            .ToApiArray();
    }

    public virtual async Task SetTyping(
        ChatId chatId,
        AuthorId authorId,
        TypingActivityKind kind,
        CancellationToken cancellationToken)
    {
        using var isolation = Computed.BeginIsolation();
        using var primer = await _listRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var activities = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        var next = activities.Without(x => x.AuthorId == authorId || x.ExpiresAt <= now);
        if (kind != TypingActivityKind.None) {
            // Preserving StartedAt across re-emits keeps the "who started first" order stable.
            var startedAt = activities.FirstOrDefault(x => x.AuthorId == authorId)?.StartedAt ?? now;
            next = next.With(new TypingActivity(authorId, startedAt, now + ActivityTtl));
        }
        else if (next.Count == activities.Count)
            return;

        await primer.Prime(next, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    // Protected, so it never travels over RPC: no shard ownership, nothing to reroute - purely local.
    [ComputeMethod]
    protected virtual Task<ApiArray<TypingActivity>> ListRaw(ChatId chatId, CancellationToken cancellationToken)
    {
        // This is the storage: the value lives in this computed, and the auto-invalidation below both
        // pins it in RAM until the last activity expires and clears it once they all have.
        var computed = Computed.GetCurrent();
        if (!_listRawPrimer.TryUsePrimed(chatId, out var activities) || activities.Count == 0)
            return Task.FromResult(ApiArray<TypingActivity>.Empty);

        computed.InvalidateSafely(activities.Max(x => x.ExpiresAt) - Clocks.SystemClock.Now, ActivityTtl);
        return Task.FromResult(activities);
    }

    // Nested types

    public sealed record TypingActivity(AuthorId AuthorId, Moment StartedAt, Moment ExpiresAt);
}
