using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// Tracks who is typing in a chat. The state is node-local RAM only - it lives in the
/// <see cref="ListRaw"/> computed - so a shard handover simply starts the new owner off empty.
/// </summary>
public partial class ChatTypingActivitiesBackend : ShardComputeService, IChatTypingActivitiesBackend
{
    // Keeps ExpireStale from waking a tick early and finding nothing to drop.
    private static readonly TimeSpan ExpirationGrace = TimeSpan.FromMilliseconds(100);

    private readonly LockingComputeMethodPrimer<ChatId, ApiArray<TypingActivity>> _listRawPrimer;

    public ChatTypingActivitiesBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
        => _listRawPrimer = new LockingComputeMethodPrimer<ChatId, ApiArray<TypingActivity>>(ListRaw);

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        // ExpireStale drops every entry as it lapses, so the freshness filter here covers just the
        // ExpirationGrace-wide gap between an expiration and the pass that removes it.
        var now = Clocks.SystemClock.Now;
        var activities = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        return activities
            .Where(x => x.ExpiresAt > now)
            .OrderBy(x => x.StartedAt)
            .Select(x => x.AuthorId)
            .ToApiArray();
    }

    public virtual async Task SetTyping(
        ChatId chatId,
        AuthorId authorId,
        TypingActivityKind kind,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        using var isolation = Computed.BeginIsolation();
        using var primer = await _listRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var activities = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        var next = activities.Without(x => x.AuthorId == authorId || x.ExpiresAt <= now);
        ttl = ttl.Clamp(default, Constants.Typing.MaxTtl);
        if (kind != TypingActivityKind.None && ttl > TimeSpan.Zero) {
            // Preserving StartedAt across renewals keeps the "who started first" order stable.
            var startedAt = activities.FirstOrDefault(x => x.AuthorId == authorId)?.StartedAt ?? now;
            next = next.With(new TypingActivity(authorId, startedAt, now + ttl));
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
        // This is the storage: the value lives in this computed, and the pending ExpireStale below is
        // what keeps it in RAM until then. Nothing primed -> nobody is typing.
        if (!_listRawPrimer.TryUsePrimed(chatId, out var activities) || activities.Count == 0)
            return Task.FromResult(ApiArray<TypingActivity>.Empty);

        var computed = Computed.GetCurrent<ApiArray<TypingActivity>>();
        _ = ExpireStale(chatId, computed, activities.Min(x => x.ExpiresAt));
        return Task.FromResult(activities);
    }

    // Private methods

    private async Task ExpireStale(
        ChatId chatId,
        Computed<ApiArray<TypingActivity>> computed,
        Moment expiresAt)
    {
        // Re-primes the survivors of the earliest expiration, which recomputes ListRaw and arms the
        // next pass. A null primer means someone else has already replaced the value.
        using var isolation = Computed.BeginIsolation();
        var clock = Clocks.SystemClock;
        try {
            await clock.Delay((expiresAt + ExpirationGrace - clock.Now).Positive(), CancellationToken.None)
                .ConfigureAwait(false);
            using var primer = await _listRawPrimer
                .TryLockAndPrepare(chatId, computed.IsConsistent, CancellationToken.None)
                .ConfigureAwait(false);
            if (primer is null)
                return;

            var activities = computed.Value;
            var next = activities.Without(x => x.ExpiresAt <= clock.Now);
            if (next.Count != activities.Count)
                await primer.Prime(next, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "ExpireStale failed for chat #{ChatId}", chatId);
        }
    }

    // Nested types

    public sealed record TypingActivity(AuthorId AuthorId, Moment StartedAt, Moment ExpiresAt);
}
