using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Locking;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Owns the one call each user is in. The record is a claim - which call, in which role - and
/// <see cref="GetUserCall"/> reads its phase off the chat's call while that call backs it. Once it doesn't,
/// the claim stays a while as Ended: that is how the client running the call learns it is over.
/// </summary>
public class CallsBackend : ShardedComputeServiceBase, ICallsBackend
{
    // Counts from the last GetUserCall that found the claim backed, so it lapses only once nobody reads
    // it any more. Long enough to outlive a ring (RingTtl), short enough that a claim left by a crashed
    // host can't outlive the call by much.
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);
    // An ended claim is read, not refreshed: it has to outlast a client that was offline when the call
    // ended, or that client reconnects to no answer at all and keeps the call's media running.
    private static readonly TimeSpan EndedTtl = TimeSpan.FromMinutes(10);
    // A claim is taken before the call it stands for exists - StartCall has to know who is free
    // before it writes the session and the invites. Until this lapses, the claim backs itself.
    private static readonly TimeSpan ClaimGrace = TimeSpan.FromSeconds(10);
    // Nothing invalidates a claim that lapsed with its Redis TTL, or one whose call ended without
    // reaching Release - so a live claim re-checks itself on this period.
    private static readonly TimeSpan ClaimSelfHeal = TimeSpan.FromSeconds(10);

    private readonly RedisScope<UserCallClaim> _userCalls;
    private readonly AsyncLockSet<UserId> _claimLocks = new(LockReentryMode.CheckedFail);

    private ILiveSessionsBackend LiveSessionsBackend
        => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    public CallsBackend(IServiceProvider services) : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _userCalls = new RedisScope<UserCallClaim>(redisDb, "live-session:user-call", Log) {
            DefaultTtl = ClaimTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<UserCallClaim?> GetUserCall(UserId userId, CancellationToken cancellationToken)
    {
        // Not SafeGet: a failed read is no answer, and "no call" ends the call on the client that runs it.
        var call = await _userCalls.Get(userId.Value).ConfigureAwait(false);
        if (call is null || call.Phase == CallPhase.Ended)
            return call;

        var computed = Computed.GetCurrent();
        var phase = await GetPhase(call, cancellationToken).ConfigureAwait(false);
        if (phase is { } p) {
            // Nothing rewrites a claim once its call connects (#4766)
            await SafeRefresh(userId).ConfigureAwait(false);
            computed.Invalidate(ClaimSelfHeal);
            return call with { Phase = p };
        }

        // The call that justified this claim is gone without its end reaching the claim (a host died
        // before ending it, the call's record lapsed): end it here rather than keep the user busy.
        Log.LogWarning("GetUserCall: ending the {Role} claim of user #{UserId} in call #{CallId}, {Age} old",
            call.Role, userId, call.CallId?.Value ?? call.ChatId.Value,
            (Clocks.SystemClock.Now - call.SinceAt).ToShortString());
        _ = EndIfUnchanged(userId, call);
        return ToEnded(call, CallOutcome.None);
    }

    public virtual async Task<bool> TryClaim(UserId userId, UserCallClaim call, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _claimLocks.Lock(userId, cancellationToken).ConfigureAwait(false)) {
            var existing = await SafeGet(userId).ConfigureAwait(false);
            if (existing is not null
                && existing.Phase != CallPhase.Ended
                && existing.CallId != call.CallId
                && await GetPhase(existing, cancellationToken).ConfigureAwait(false) is not null)
                return false;

            await _userCalls
                .Set(userId.Value, call with { SinceAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            Invalidate(userId);
            return true;
        }
    }

    public virtual async Task EndCall(
        UserId userId, CallId callId, CallOutcome outcome, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _claimLocks.Lock(userId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGet(userId).ConfigureAwait(false);
            if (call is null || call.CallId != callId)
                return;

            // Already ended - unless only by the self-heal, which can get there between the call's end and
            // this, and knows nothing of how it went.
            if (call.Phase == CallPhase.Ended && (call.Outcome != CallOutcome.None || outcome == CallOutcome.None))
                return;

            await _userCalls.Set(userId.Value, ToEnded(call, outcome), EndedTtl).ConfigureAwait(false);
            Invalidate(userId);
        }
    }

    // Private methods

    // Null when the chat's call no longer backs the claim. The phase stored with the claim is only its
    // initial one, and holds only for ClaimGrace, before the call exists.
    private async Task<CallPhase?> GetPhase(UserCallClaim call, CancellationToken cancellationToken)
    {
        var liveCall = await LiveSessionsBackend.GetCall(call.ChatId, cancellationToken).ConfigureAwait(false);
        var live = liveCall is null
            ? null
            : await LiveSessionsBackend.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        var invites = liveCall is null || call.Role != CallRole.Callee
            ? default
            : await LiveSessionsBackend.ListInvites(call.ChatId, cancellationToken).ConfigureAwait(false);
        var phase = GetPhase(call, liveCall, live, invites);
        if (phase is null && Clocks.SystemClock.Now - call.SinceAt < ClaimGrace)
            return call.Phase;

        return phase;
    }

    internal static CallPhase? GetPhase(
        UserCallClaim call, LiveCall? liveCall, LiveSession? live, ApiArray<CallInvite> invites)
    {
        if (liveCall is null)
            return null;

        // The chat is in another call by now: that one, however live, isn't this claim's.
        if (call.CallId is { } callId && callId != liveCall.Id)
            return null;

        var isPresent = live?.Members.Any(m => m.AuthorId == call.AuthorId && (m.IsMicOpen || m.IsListening))
            ?? false;
        if (call.Role == CallRole.Callee) {
            var invite = invites.FirstOrDefault(i => i.InviteeId == call.AuthorId);
            return invite?.Status switch {
                CallInviteStatus.Ringing => CallPhase.Ringing,
                CallInviteStatus.Accepted or CallInviteStatus.Active => CallPhase.Active,
                _ => liveCall.IsAnswered && isPresent ? CallPhase.Active : null,
            };
        }

        // An outcome means the call is ending: it backs nothing, however long its record takes to go.
        var isOwnCall = liveCall.CallerId == call.AuthorId;
        if (!liveCall.IsAnswered)
            return isOwnCall && liveCall.Outcome == CallOutcome.None ? CallPhase.Dialing : null;

        return isOwnCall || isPresent ? CallPhase.Active : null;
    }

    // The claim was judged from a read that a TryClaim may have overtaken since: whatever is there now
    // is another call's claim, not this stale one.
    private async Task EndIfUnchanged(UserId userId, UserCallClaim call)
    {
        try {
            using (Computed.BeginIsolation())
            using (await _claimLocks.Lock(userId, CancellationToken.None).ConfigureAwait(false)) {
                if (await SafeGet(userId).ConfigureAwait(false) != call)
                    return;

                await _userCalls.Set(userId.Value, ToEnded(call, CallOutcome.None), EndedTtl).ConfigureAwait(false);
                Invalidate(userId);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to end the stale call claim of user #{UserId}", userId);
        }
    }

    private UserCallClaim ToEnded(UserCallClaim call, CallOutcome outcome)
        => call with { Phase = CallPhase.Ended, Outcome = outcome, SinceAt = Clocks.SystemClock.Now };

    private async Task<UserCallClaim?> SafeGet(UserId userId)
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
