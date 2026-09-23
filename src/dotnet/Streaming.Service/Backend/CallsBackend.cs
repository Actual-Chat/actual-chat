using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Locking;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Owns the one call each user is in. The record is a claim: <see cref="GetUserCall"/> answers
/// with it only while the chat's live session still backs it.
/// </summary>
public class CallsBackend : ShardComputeService, ICallsBackend
{
    // Long enough to outlive a ring (RingTtl) and the gap between presence ticks, short enough
    // that a claim left by a crashed host can't outlive the call by much.
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);
    // A claim is taken before the call it stands for exists - StartCall has to know who is free
    // before it writes the session and the invites. Until this lapses, the claim backs itself.
    // SetPhase restarts it: the session that backs the new phase lives on the chat's shard, and its
    // invalidation can reach this shard after the recompute SetPhase triggers (#4749).
    private static readonly TimeSpan ClaimGrace = TimeSpan.FromSeconds(10);
    // Nothing invalidates a claim that lapsed with its Redis TTL, or one whose call ended without
    // reaching Release - so a live claim re-checks itself on this period.
    private static readonly TimeSpan ClaimSelfHeal = TimeSpan.FromSeconds(10);

    private readonly RedisScope<UserCall> _userCalls;
    private readonly AsyncLockSet<UserId> _claimLocks = new(LockReentryMode.CheckedFail);

    private ILiveSessionsBackend LiveSessionsBackend
        => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    public CallsBackend(IServiceProvider services) : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _userCalls = new RedisScope<UserCall>(redisDb, "live-session:user-call", Log) {
            DefaultTtl = ClaimTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<UserCall?> GetUserCall(UserId userId, CancellationToken cancellationToken)
    {
        // Captured before the awaits below, as in LiveSessionsBackend.GetState.
        var computed = Computed.GetCurrent();
        await ShardOwner.RequireShardOwnership(userId, addDependency: true, cancellationToken)
            .ConfigureAwait(false);

        var call = await SafeGet(userId).ConfigureAwait(false);
        if (call is null)
            return null;
        if (await IsBacked(call, cancellationToken).ConfigureAwait(false)) {
            computed.Invalidate(ClaimSelfHeal);
            return call;
        }

        // The session that justified this claim is gone (the client crashed mid-call, a host died
        // before releasing it): drop it rather than keep the user busy until the TTL lapses.
        _ = Release(userId, call.ChatId, CancellationToken.None);
        return null;
    }

    public virtual async Task<bool> TryClaim(UserId userId, UserCall call, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _claimLocks.Lock(userId, cancellationToken).ConfigureAwait(false)) {
            var existing = await SafeGet(userId).ConfigureAwait(false);
            if (existing is not null
                && existing.ChatId != call.ChatId
                && await IsBacked(existing, cancellationToken).ConfigureAwait(false))
                return false;

            await _userCalls
                .Set(userId.Value, call with { SinceAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            Invalidate(userId);
            return true;
        }
    }

    public virtual async Task SetPhase(
        UserId userId, ChatId chatId, CallPhase phase, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _claimLocks.Lock(userId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGet(userId).ConfigureAwait(false);
            if (call is null || call.ChatId != chatId)
                return;
            if (call.Phase == phase) {
                await _userCalls.Refresh(userId.Value).ConfigureAwait(false);
                return;
            }

            await _userCalls
                .Set(userId.Value, call with { Phase = phase, SinceAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            Invalidate(userId);
        }
    }

    public virtual async Task Release(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _claimLocks.Lock(userId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGet(userId).ConfigureAwait(false);
            if (call is null || call.ChatId != chatId)
                return;

            await _userCalls.Remove(userId.Value).ConfigureAwait(false);
            Invalidate(userId);
        }
    }

    // Private methods

    private async Task<bool> IsBacked(UserCall call, CancellationToken cancellationToken)
    {
        if (Clocks.SystemClock.Now - call.SinceAt < ClaimGrace)
            return true;

        var live = await LiveSessionsBackend.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        if (live is not { Kind: LiveSessionKind.Call })
            return false;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == call.AuthorId);
        if (call.Phase == CallPhase.Ringing)
            return invite is { Status: CallInviteStatus.Ringing };
        if (call.Phase == CallPhase.Dialing) {
            // The status matters as much as the caller: a resolved one (no answer, declined, busy)
            // lingers past the call, and would otherwise keep the caller busy for as long as it lives.
            var callState = await LiveSessionsBackend
                .GetCallState(call.ChatId, cancellationToken)
                .ConfigureAwait(false);
            return callState is { Status: CallStatus.Dialing or CallStatus.Connecting }
                && callState.CallerId == call.AuthorId;
        }

        // Active: answered, or present in the conversation - the caller has no invite of their own.
        return invite is { Status: CallInviteStatus.Accepted or CallInviteStatus.Active }
            || live.Members.Any(m => m.AuthorId == call.AuthorId && (m.IsMicOpen || m.IsListening));
    }

    private async Task<UserCall?> SafeGet(UserId userId)
    {
        try {
            return await _userCalls.Get(userId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read the call of user #{UserId} from Redis", userId);
            return null;
        }
    }

    private void Invalidate(UserId userId)
    {
        using (Invalidation.Begin())
            _ = GetUserCall(userId, default);
    }
}
