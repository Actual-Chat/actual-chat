using ActualChat.Live;

namespace ActualChat.Streaming;

/// <summary>
/// Emoji reactions sent in a chat's call. Like <see cref="ChatTypingActivitiesBackend"/>, the state is
/// node-local RAM only - it lives in the <see cref="ListRaw"/> computed - so a shard handover drops it.
/// </summary>
public partial class ChatCallReactionsBackend : ShardComputeService, IChatCallReactionsBackend
{
    // Keeps ExpireStale from waking a tick early and finding nothing to drop.
    private static readonly TimeSpan ExpirationGrace = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReactionDuration = Constants.Call.ReactionDuration;
    private static readonly TimeSpan ReactionMinInterval = Constants.Call.ReactionMinInterval;

    private readonly LockingComputeMethodPrimer<ChatId, ApiArray<CallReaction>> _listRawPrimer;

    public ChatCallReactionsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
        => _listRawPrimer = new LockingComputeMethodPrimer<ChatId, ApiArray<CallReaction>>(ListRaw);

    // [ComputeMethod]
    public virtual async Task<ApiArray<CallReaction>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        // ExpireStale drops every reaction as it lapses, so the freshness filter here covers just the
        // ExpirationGrace-wide gap between an expiration and the pass that removes it.
        var minSentAt = Clocks.SystemClock.Now - ReactionDuration;
        var reactions = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        return reactions.Where(x => x.SentAt > minSentAt).OrderBy(x => x.SentAt).ToApiArray();
    }

    public virtual async Task Send(ChatId chatId, AuthorId authorId, Emoji emoji, CancellationToken cancellationToken)
    {
        using var isolation = Computed.BeginIsolation();
        using var primer = await _listRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var reactions = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        var previous = reactions.FirstOrDefault(x => x.AuthorId == authorId);
        if (previous is not null && now - previous.SentAt < ReactionMinInterval)
            return;

        var minSentAt = now - ReactionDuration;
        var next = reactions
            .Without(x => x.AuthorId == authorId || x.SentAt <= minSentAt)
            .With(new CallReaction(authorId, emoji, now));
        await primer.Prime(next, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    // Protected, so it never travels over RPC: no shard ownership, nothing to reroute - purely local.
    [ComputeMethod]
    protected virtual Task<ApiArray<CallReaction>> ListRaw(ChatId chatId, CancellationToken cancellationToken)
    {
        // This is the storage: the value lives in this computed, and the pending ExpireStale below is
        // what keeps it in RAM until then. Nothing primed -> no reactions.
        if (!_listRawPrimer.TryUsePrimed(chatId, out var reactions) || reactions.Count == 0)
            return Task.FromResult(ApiArray<CallReaction>.Empty);

        var computed = Computed.GetCurrent<ApiArray<CallReaction>>();
        _ = ExpireStale(chatId, computed, reactions.Min(x => x.SentAt) + ReactionDuration);
        return Task.FromResult(reactions);
    }

    // Private methods

    private async Task ExpireStale(
        ChatId chatId,
        Computed<ApiArray<CallReaction>> computed,
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

            var minSentAt = clock.Now - ReactionDuration;
            var reactions = computed.Value;
            var next = reactions.Without(x => x.SentAt <= minSentAt);
            if (next.Count != reactions.Count)
                await primer.Prime(next, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "ExpireStale failed for chat #{ChatId}", chatId);
        }
    }
}
