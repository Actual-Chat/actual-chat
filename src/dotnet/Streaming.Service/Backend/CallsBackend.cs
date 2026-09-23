using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Locking;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Owns the one call each user is in. The record is a claim - which call, in which role - and
/// <see cref="GetUserCall"/> reads its phase off the chat's live session, answering only while that
/// session still backs it.
/// </summary>
public class CallsBackend : ShardComputeService, ICallsBackend
{
    // Counts from the last GetUserCall that found the claim backed, so it lapses only once nobody reads
    // it any more. Long enough to outlive a ring (RingTtl), short enough that a claim left by a crashed
    // host can't outlive the call by much.
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);
    // A claim is taken before the call it stands for exists - StartCall has to know who is free
    // before it writes the session and the invites. Until this lapses, the claim backs itself.
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

        var phase = await GetPhase(call, cancellationToken).ConfigureAwait(false);
        if (phase is { } p) {
            // Nothing rewrites a claim once its call connects (#4766)
            await SafeRefresh(userId).ConfigureAwait(false);
            computed.Invalidate(ClaimSelfHeal);
            return call with { Phase = p };
        }

        // The session that justified this claim is gone (the client crashed mid-call, a host died
        // before releasing it): drop it rather than keep the user busy until the TTL lapses.
        Log.LogWarning("GetUserCall: dropping the {Role} claim of user #{UserId} in chat #{ChatId}, {Age} old",
            call.Role, userId, call.ChatId, (Clocks.SystemClock.Now - call.SinceAt).ToShortString());
        _ = ReleaseIfUnchanged(userId, call);
        return null;
    }

    public virtual async Task<bool> TryClaim(UserId userId, UserCall call, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _claimLocks.Lock(userId, cancellationToken).ConfigureAwait(false)) {
            var existing = await SafeGet(userId).ConfigureAwait(false);
            if (existing is not null
                && existing.ChatId != call.ChatId
                && await GetPhase(existing, cancellationToken).ConfigureAwait(false) is not null)
                return false;

            await _userCalls
                .Set(userId.Value, call with { SinceAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            Invalidate(userId);
            return true;
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

    // Null when the chat's live session no longer backs the claim. The phase stored with the claim
    // is only its initial one, and holds only for ClaimGrace, before the session exists.
    private async Task<CallPhase?> GetPhase(UserCall call, CancellationToken cancellationToken)
    {
        var live = await LiveSessionsBackend.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        var callState = call.Role == CallRole.Caller
            ? await LiveSessionsBackend.GetCallState(call.ChatId, cancellationToken).ConfigureAwait(false)
            : null;
        var phase = GetPhase(call, live, callState);
        if (phase is null && Clocks.SystemClock.Now - call.SinceAt < ClaimGrace)
            return call.Phase;

        return phase;
    }

    internal static CallPhase? GetPhase(UserCall call, LiveSession? live, CallState? callState)
    {
        if (live is not { Kind: LiveSessionKind.Call })
            return null;

        var isConnected = live.Conversation is not null;
        var isPresent = live.Members.Any(m => m.AuthorId == call.AuthorId && (m.IsMicOpen || m.IsListening));
        if (call.Role == CallRole.Callee) {
            var invite = live.Invites.FirstOrDefault(i => i.InviteeId == call.AuthorId);
            return invite?.Status switch {
                CallInviteStatus.Ringing => CallPhase.Ringing,
                CallInviteStatus.Accepted or CallInviteStatus.Active => CallPhase.Active,
                _ => isConnected && isPresent ? CallPhase.Active : null,
            };
        }

        // A resolved status (no answer, declined, busy) outlives the call, so it backs nothing.
        var isOwnCall = callState?.CallerId == call.AuthorId;
        if (isOwnCall && callState!.Status == CallStatus.Dialing)
            return CallPhase.Dialing;

        var isAnswered = isOwnCall && callState!.Status is CallStatus.Connecting or CallStatus.Active;
        return isConnected && (isAnswered || isPresent) ? CallPhase.Active : null;
    }

    // The claim was judged from a read that a TryClaim may have overtaken since: whatever is there now
    // is another call's claim, not this stale one.
    private async Task ReleaseIfUnchanged(UserId userId, UserCall call)
    {
        try {
            using (Computed.BeginIsolation())
            using (await _claimLocks.Lock(userId, CancellationToken.None).ConfigureAwait(false)) {
                if (await SafeGet(userId).ConfigureAwait(false) != call)
                    return;

                await _userCalls.Remove(userId.Value).ConfigureAwait(false);
                Invalidate(userId);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to drop the stale call claim of user #{UserId}", userId);
        }
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

    private async Task SafeRefresh(UserId userId)
    {
        try {
            await _userCalls.Refresh(userId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to extend the call claim of user #{UserId}", userId);
        }
    }

    private void Invalidate(UserId userId)
    {
        using (Invalidation.Begin())
            _ = GetUserCall(userId, default);
    }
}
