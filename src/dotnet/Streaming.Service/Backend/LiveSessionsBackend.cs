using ActualChat.Streaming.Diagnostics;
using ActualChat.Comparison;
using ActualChat.Flows;
using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.Queues;
using ActualChat.Redis;
using ActualLab.Locking;
using ActualLab.Redis;
using ActualLab.Versioning;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend for the single live conversation per chat: its in-progress summary block,
/// the participant registry, and open/close driven by live audio/video streams.
/// </summary>
public partial class LiveSessionsBackend : ShardedComputeServiceBase, ILiveSessionsBackend
{
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan SelfHealDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ParticipantStaleness = TimeSpan.FromSeconds(90);
    // Safety net: if a closing transcription-on conversation isn't finalized by
    // LiveConversationSummaryFlow within this window (flow not scheduled when global
    // summarization is off, or it threw), the backend vanishes it and sends FINAL itself.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(90);
    // Grace once nobody is recording/streaming before a phone-mode session winds down.
    private static readonly TimeSpan RecordingCloseGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RingTimeout = Constants.Call.RingTimeout;
    private static readonly TimeSpan RingTtl = Constants.Call.RingTtl;
    // How long past RingTimeout a missed ring can still be answered. The callee's ring stops on time,
    // but an Answer tapped at its very end reaches us only after a cold start and the RPC connect.
    private static readonly TimeSpan AnswerGrace = TimeSpan.FromSeconds(10);
    // An unanswered call's record outlives its ring and the late-answer window, then lapses on its own.
    private static readonly TimeSpan RingingCallTtl = RingTtl + AnswerGrace;
    // How long an in-progress (Dialing/Connecting/Active) call state lingers with no observer before
    // its Redis key lapses; a terminal transition overwrites it sooner.
    private static readonly TimeSpan DialingStateTtl = TimeSpan.FromSeconds(60);
    // How long the caller keeps being shown a resolved call status (accepted / declined / no answer),
    // once the session itself is gone.
    private static readonly TimeSpan ResolvedStateTtl = TimeSpan.FromSeconds(30);
    // How long AcceptCall waits before checking that the invitee genuinely connected (see
    // EnforceCallConnectGrace) - short enough that a stalled connect surfaces fast, long enough to
    // cover the accept-flow reorder's round trip (client starts listening immediately on accept).
    private static readonly TimeSpan CallConnectGrace = TimeSpan.FromSeconds(5);
    // How long a call left with one party waits before closing (see EnforceCallLeaveGrace): a party whose
    // presence dips and returns within it keeps the call, a real hang-up still ends it within seconds.
    private static readonly TimeSpan CallLeaveGrace = TimeSpan.FromSeconds(2);
    // How long CallTailFlow's first pass waits: the realtime transcriber's own post-audio deadline is
    // how long the last utterance's entry can take to appear at all (its text settles later still).
    private static readonly TimeSpan CallTailDelay = Constants.Transcription.CompletionTimeout;
    // How long a call into an ongoing session waits for its parties' clients to stop their media before its
    // entry is written anyway: past it, a client is offline or stuck, and the entry must not wait on it.
    private static readonly TimeSpan CallStreamsEndTimeout = TimeSpan.FromSeconds(10);

    private readonly RedisScope<LiveSessionState> _redisScope;
    private readonly RedisScope<LiveCall> _calls;
    private readonly RedisScope<CallState> _callStates;
    private readonly RedisMultiHashMap<ParticipationInfo> _participants;
    private readonly RedisMultiHashMap<CallInvite> _invites;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);
    private long _lastCallLocalId;

    private IAuthorsBackend AuthorsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }
    private ICallsBackend CallsBackend => field ??= Services.GetRequiredService<ICallsBackend>();
    private ICommander Commander => field ??= Services.Commander();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    public LiveSessionsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        LiveAudioBackend = services.GetRequiredService<ILiveAudioBackend>();
        LiveVideoBackend = services.GetRequiredService<ILiveVideoBackend>();
        VersionGenerator = services.GetRequiredService<VersionGenerator<long>>();
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _redisScope = new RedisScope<LiveSessionState>(redisDb, "live-session:state", Log) {
            DefaultTtl = KeyTtl,
        };
        _calls = new RedisScope<LiveCall>(redisDb, "live-session:call", Log) {
            DefaultTtl = KeyTtl,
        };
        _callStates = new RedisScope<CallState>(redisDb, "live-session:call-state", Log) {
            DefaultTtl = ResolvedStateTtl,
        };
        _participants = new RedisMultiHashMap<ParticipationInfo>(redisDb, "live-session:participants", Log) {
            HashTtl = KeyTtl,
        };
        _invites = new RedisMultiHashMap<CallInvite>(redisDb, "live-session:invites", Log) {
            HashTtl = KeyTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<LiveSessionState?> GetState(ChatId chatId, CancellationToken cancellationToken)
    {
        var redisReadAt = CpuTimestamp.Now;
        var state = await SafeGet(chatId).ConfigureAwait(false);
        var redisReadMs = (long)redisReadAt.Elapsed.TotalMilliseconds;
        if (redisReadMs > 250)
            Log.LogWarning(
                "GetState: Redis read took {ReadMs}ms for chat #{ChatId}",
                redisReadMs, chatId);
        if (state is null)
            return null;

        // Transcription keeps the longer grace so LiveConversationSummaryFlow can finalize;
        // a phone-mode session winds down on the shorter recording grace.
        var grace = state.TranscriptionOn ? CloseTimeout : RecordingCloseGrace;
        if (state is { IsClosing: true, ClosingAt: { } closingAt }
            && Clocks.SystemClock.Now - closingAt > grace) {
            _ = SelfClose(chatId);
            return null;
        }

        // Liveness is streaming-driven: once nobody is recording audio or video, begin the close grace.
        if (!state.IsClosing && !await IsSessionLive(chatId).ConfigureAwait(false))
            _ = StartClosingGrace(chatId);

        // Required, not merely defensive: IsSessionLive reads raw Redis against a time-based staleness
        // cutoff, so nothing invalidates this on its own. The churn it creates is filtered before it can
        // reach the conversation metadata cache - see GetConsolidatedVisibleStartLid.
        Computed.GetCurrent().InvalidateSafely(SelfHealDelay);
        return state;
    }

    // [ComputeMethod]
    public virtual Task<long?> GetVisibleStartLid(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedVisibleStartLid(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<Conversation?> GetLiveConversation(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedLiveConversation(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        // A call is shown from its first ring: with no session yet it is a Call with no conversation,
        // and placed into a session it overlays that session's own view until it ends.
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        var call = await GetCall(chatId, cancellationToken).ConfigureAwait(false);
        if (state is { SessionStartedAt: null })
            state = null;
        if (state is null && call is null)
            return null;

        var audio = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var video = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var host = state is null ? call!.CallerId : state.Host ?? state.AuthorIds[0];
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        // Stands in as JoinedAt for stream-only members - they have no participation record to date the
        // join with. Never Clocks.Now: that makes every recompute a different LiveSession, so no
        // consumer can consolidate a no-op invalidation away.
        var startedAt = state?.SessionStartedAt ?? call!.StartedAt;

        var byAuthor = new Dictionary<AuthorId, LiveSessionMember>();
        LiveSessionMember For(AuthorId a)
            => byAuthor.TryGetValue(a, out var m) ? m : new() { AuthorId = a, JoinedAt = startedAt };

        if (state is null)
            byAuthor[call!.CallerId] = For(call.CallerId);

        foreach (var s in audio)
            byAuthor[s.AuthorId] = For(s.AuthorId) with { IsMicOpen = true };
        foreach (var v in video) {
            var m = For(v.AuthorId);
            byAuthor[v.AuthorId] = m with {
                HasCamera = m.HasCamera || v.SourceKind == VideoSourceKind.Camera,
                HasScreenShare = m.HasScreenShare || v.SourceKind == VideoSourceKind.ScreenCast,
            };
        }
        foreach (var (authorIdValue, info) in participants) {
            if (info is null)
                continue;
            if (!AuthorId.TryParse(authorIdValue, out var authorId))
                continue;

            var m = For(authorId);
            var isRecorder = IsFreshRecorder(info, cutoff);
            var isListener = info.Kind == ParticipationKind.AudioListen && info.RegisteredAt >= cutoff;
            byAuthor[authorId] = m with {
                IsMicOpen = m.IsMicOpen || isRecorder,
                IsListening = m.IsListening || isListener,
                MicMuted = info.MicMuted,
                HandRaisedAt = info.HandRaisedAt,
                // RegisteredAt is a liveness stamp rewritten by every heartbeat - only the fallback
                // for participation records written before JoinedAt existed.
                JoinedAt = info.JoinedAt == default ? info.RegisteredAt : info.JoinedAt,
            };
        }
        // Owners and Moderators are grouped with the host (they can manage the call too).
        var ownerIds = (await RolesBackend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Owner, cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet();
        var moderatorIds = await RolesBackend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Moderator, cancellationToken)
            .ConfigureAwait(false);
        ownerIds.UnionWith(moderatorIds);

        var members = byAuthor.Values
            .Select(m => m with {
                Group = m.AuthorId == host || ownerIds.Contains(m.AuthorId) ? MemberGroup.Host
                    : m.IsPresent ? MemberGroup.Other
                    : MemberGroup.Exited,
                // Only present members show a hand: a crashed client's record lingers for ParticipantStaleness
                HandRaisedAt = m.IsPresent ? m.HandRaisedAt : null,
            })
            // Without the tie-breaks the order is byAuthor's insertion order, i.e. Redis HGETALL
            // order - it reshuffles the list on every recompute. AuthorId breaks the JoinedAt tie
            // stream-only members share via the startedAt fallback.
            .OrderBy(m => (int)m.Group)
            .ThenBy(m => m.JoinedAt)
            .ThenBy(m => m.AuthorId.Value, StringComparer.Ordinal)
            .ToList();

        return new LiveSession {
            ChatId = chatId,
            Host = host,
            StartedAt = startedAt,
            Rules = state?.Rules ?? SessionRules.Default,
            Members = members,
            Conversation = state?.ToConversation(),
            TranscriptionOn = state?.TranscriptionOn ?? false,
            Version = Math.Max(state?.Version ?? 0, call?.Version ?? 0),
            Kind = call is null ? state!.Kind : LiveSessionKind.Call,
        };
    }

    // [ComputeMethod]
    public virtual Task<ApiArray<AuthorId>> ListParticipants(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedParticipants(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken)
        => GetConsolidatedHasRecorder(chatId, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<CallState?> GetCallState(ChatId chatId, CancellationToken cancellationToken)
    {
        // Captured before the awaits below — see GetState.
        var computed = Computed.GetCurrent();
        // Depend on GetCall so CallState and the call invalidate together over RPC instead of drifting
        // independently - this used to race (9e0b87186c): Accepted could land before the call did.
        await GetCall(chatId, cancellationToken).ConfigureAwait(false);

        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        if (callState is null)
            return null;

        // Nothing invalidates a Redis TTL expiry, so age the observed value out alongside the key.
        var expiresIn = callState.ChangedAt + CallStateTtl(callState.Status) - Clocks.SystemClock.Now;
        if (expiresIn <= TimeSpan.Zero)
            return null;

        computed.Invalidate(expiresIn);
        return callState;
    }

    // [ComputeMethod]
    public virtual async Task<LiveCall?> GetCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Not SafeGetCall: a failed read is no answer, while "no call" ends every claim on it - and the call
        // on the clients that run it.
        var call = await _calls.Get(chatId.Value).ConfigureAwait(false);
        if (call is null)
            return null;

        // Rings lapse and parties drop without a write that would invalidate this, so while the call is
        // observed it re-checks itself; the ring timer and the record's own TTL cover the unobserved case.
        if (await HasStaleRinging(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);
        else if (!call.IsAnswered && !await HasFreshRing(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);
        if (call.IsAnswered) {
            _ = SyncCallParticipantActivity(chatId, call, CancellationToken.None);
            // A crashed client never reports leaving: this is what ends the call it was the other side of.
            if (!await IsCallAlive(chatId, call).ConfigureAwait(false))
                _ = EndCall(chatId, mustRecheckParties: true, call.Id);
        }
        Computed.GetCurrent().InvalidateSafely(SelfHealDelay);
        return call;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CallInvite>> ListInvites(ChatId chatId, CancellationToken cancellationToken)
    {
        // Invites outlast their call until its teardown drops them; without the call they're nobody's.
        var call = await GetCall(chatId, cancellationToken).ConfigureAwait(false);
        if (call is null)
            return [];

        // Not SafeGetInvites, as in GetCall: a callee's claim missing its invite is ended.
        var invites = await _invites.GetHashMap(chatId.Value).ConfigureAwait(false);
        return invites.Values.SkipNullItems().OrderBy(i => i.RingingAt).ToApiArray();
    }

    public virtual async Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool hasText,
        bool hasVoice,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();

        // Best-effort wake trigger: per utterance (before the dedup/early-return below), voice-only,
        // and never allowed to fail stream registration.
        if (hasVoice)
            try {
                await Services.Queues()
                    .Enqueue(new SpeechStartedEvent(chatId, authorId, Clocks.SystemClock.Now), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Failed to enqueue SpeechStartedEvent for chat '{ChatId}'", chatId);
            }

        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null) {
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            var startEntryLid = entryLid
                ?? (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            state = new LiveSessionState {
                ChatId = chatId,
                StartEntryLid = startEntryLid,
                EndEntryLid = startEntryLid,
                StartedAt = now,
                AuthorIds = [authorId],
                Host = authorId,
                TranscriptionOn = chat?.IsSummarized ?? false,
                HasTranscript = hasText,
                Version = VersionGenerator.NextVersion(),
            };
        }
        else {
            var authorIds = state.AuthorIds.Contains(authorId)
                ? state.AuthorIds
                : [..state.AuthorIds, authorId];
            var hasTranscript = state.HasTranscript || hasText;
            if (ReferenceEquals(authorIds, state.AuthorIds)
                && !state.IsClosing
                && hasTranscript == state.HasTranscript) {
                // Nothing to write, but a re-registering stream is proof of life: keep the key alive.
                await _redisScope.Refresh(chatId.Value).ConfigureAwait(false);
                return;
            }

            state = state with {
                AuthorIds = authorIds,
                HasTranscript = hasTranscript,
                IsClosing = false,
                ClosingAt = null,
                Version = VersionGenerator.NextVersion(state.Version),
            };
        }

        if (state.SessionStartedAt is null && state.AuthorIds.Count >= 2) {
            var visibleStartLid = (await ChatsBackend
                .GetLidRange(chatId, false, cancellationToken)
                .ConfigureAwait(false)).End;
            state = state with {
                SessionStartedAt = now,
                VisibleStartLid = visibleStartLid,
                Version = VersionGenerator.NextVersion(state.Version),
            };
            // Calls announce themselves by ringing, not a conversation banner; only ambient sessions banner.
            if (state.Kind == LiveSessionKind.Ambient)
                await EnqueueLiveNotification(
                    state, ConversationNotificationPhase.Started, "Voice chat started", cancellationToken)
                    .ConfigureAwait(false);
        }

        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        // Register the streamer as a participant so per-peer mute flags have a home
        // (grouping uses live-stream state, so a stale entry never misgroups an active streamer).
        await EnsureParticipant(chatId, authorId).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task SetParticipation(
        ChatId chatId,
        AuthorId authorId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        bool emptiedByLeave;
        int? callPartiesLeft = null;
        var startedClosing = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            if (isActive) {
                // Preserve mute flags, the raised hand and the original join time across heartbeats / kind
                // changes: RegisteredAt is refreshed by every heartbeat, so it can't double as JoinedAt.
                var now = Clocks.SystemClock.Now;
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                var info = existing is null
                    ? new ParticipationInfo(kind, now, JoinedAt: now)
                    : existing with {
                        Kind = kind,
                        RegisteredAt = now,
                        JoinedAt = existing.JoinedAt != default ? existing.JoinedAt : now,
                    };
                await _participants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
                // The participants hash refreshes its own TTL on write, but the session state key only
                // does so on Set - which a steady-state session never reaches. Without this the state
                // silently expires mid-call and the next stream rebuilds it as a brand-new session.
                await _redisScope.Refresh(chatId.Value).ConfigureAwait(false);
                // An answered call lives as long as its session does; a ringing one keeps its own short TTL.
                if (await SafeGetCall(chatId).ConfigureAwait(false) is { IsAnswered: true })
                    await _calls.Refresh(chatId.Value).ConfigureAwait(false);
            }
            else {
                // Kind-guarded: a stream ending must only clear the registration it itself owns. Two
                // independent streams for the same author (e.g. a recorder stopping while a separate
                // listening stream stays open) would otherwise let the ending one delete the record the
                // still-open one relies on - _participants holds one record per author, not per kind.
                // Nor is such an ending a departure: the author is still here under the newer kind, so
                // the leave handling below (which closes a session nobody streams in) must not run.
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                if (existing is { } info && info.Kind != kind)
                    return;

                if (existing is not null)
                    await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            }
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
            InvalidateGet(chatId);
            // Whether the departure was an explicit hang-up or a connection that just died - both reach
            // this the same way. Dialing keeps its own ExpireRings path.
            if (!isActive)
                callPartiesLeft = await CountCallPartiesLeft(chatId, authorId, cancellationToken)
                    .ConfigureAwait(false);
            // A join/heartbeat, or a leave with someone still streaming, just re-evaluates liveness; the
            // grace there is the safety net for crashed/stale clients. A leave that stops the last stream
            // closes it outright below - no waiting on the grace or on a UI observer. EvaluateLiveness only
            // marks a still-populated session closing (recoverable if a recorder returns), so a transient
            // not-live blip never tears down a live recording - unlike an unconditional CloseNow here would.
            // A call leave skips all of this: it would close or mark closing the call CallLeaveGrace may keep.
            var isCallLeave = callPartiesLeft is 0 or 1;
            emptiedByLeave = !isActive && !isCallLeave && !await IsSessionLive(chatId).ConfigureAwait(false);
            if (!emptiedByLeave && !isCallLeave)
                startedClosing = await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        if (callPartiesLeft is { } partiesLeft and (0 or 1))
            await OnCallPartyLeft(chatId, partiesLeft).ConfigureAwait(false);
        else if (emptiedByLeave)
            await CloseNow(chatId).ConfigureAwait(false);
        else if (startedClosing)
            // The last recorder stayed on as a listener: no stream left to trip CloseNow, but the session is
            // no longer live. Wake the summary flow to finalize the just-closing session now, rather than
            // leaving the block and Call tab up until the 90s SelfClose backstop fires.
            await WakeSummaryFlow(chatId).ConfigureAwait(false);
    }

    public virtual async Task SetRules(ChatId chatId, SessionRules rules, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return;

        state = state with { Rules = rules, Version = VersionGenerator.NextVersion(state.Version) };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task MutePeer(
        ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken)
    {
        await EnsureParticipant(chatId, targetAuthorId).ConfigureAwait(false);
        var existing = await SafeGetParticipant(chatId, targetAuthorId).ConfigureAwait(false);
        if (existing is null)
            return;

        await _participants
            .Set(chatId.Value, targetAuthorId.Value, existing with { MicMuted = muted })
            .ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task MuteAll(
        ChatId chatId,
        ApiArray<AuthorId> exceptAuthorIds,
        bool muted,
        CancellationToken cancellationToken)
    {
        // Soft "mute all except the actor": sets MicMuted on every other participant.
        // This is peer-revocable — a muted peer can re-record to unmute themselves.
        var exceptAuthorIdValues = exceptAuthorIds.Select(x => x.Value).ToHashSet();
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        foreach (var (authorIdValue, info) in participants) {
            if (info is null || info.MicMuted == muted)
                continue;
            if (exceptAuthorIdValues.Contains(authorIdValue))
                continue;

            await _participants.Set(chatId.Value, authorIdValue, info with { MicMuted = muted }).ConfigureAwait(false);
        }
        InvalidateGet(chatId);
    }

    public virtual async Task SetHost(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null || state.Host == authorId)
            return;

        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        if (!participants.ContainsKey(authorId.Value))
            throw StandardError.Constraint("Only a call participant can become the call host.");

        state = state with {
            Host = authorId,
            Version = VersionGenerator.NextVersion(state.Version),
        };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task SetHandRaised(
        ChatId chatId,
        AuthorId authorId,
        bool isRaised,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
        if (existing is null || (existing.HandRaisedAt is not null) == isRaised)
            return;

        if (isRaised) {
            // A hand is a LiveSession member's state, so it exists only where Get returns a session
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is not { SessionStartedAt: not null })
                return;
        }

        var info = existing with { HandRaisedAt = isRaised ? Clocks.SystemClock.Now : null };
        await _participants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task LowerAllHands(ChatId chatId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        foreach (var (authorIdValue, info) in participants) {
            if (info?.HandRaisedAt is null)
                continue;

            await _participants
                .Set(chatId.Value, authorIdValue, info with { HandRaisedAt = null })
                .ConfigureAwait(false);
        }
        InvalidateGet(chatId);
    }

    public virtual async Task UpdateSummary(
        ChatId chatId,
        LiveSessionSummary summary,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return;

        // The summary flow re-runs on a fixed schedule, so most calls carry an unchanged summary. Bailing
        // out here keeps LastSummaryAt/Version from making every run look like a change and invalidating
        // the whole live view for nothing.
        var isUnchanged = state.Title == summary.Title
            && state.Description == summary.Description
            && state.Summary == summary.Summary
            && state.EndEntryLid == summary.EndEntryLid
            && state.MessageCount == summary.MessageCount
            && state.IsExpandedByDefault == summary.IsExpandedByDefault
            && (summary.AuthorIds.Count == 0 || state.AuthorIds.SequenceEqual(summary.AuthorIds));
        if (isUnchanged)
            return;

        // The first non-empty title promotes the live banner to "Voice chat: {title}" (TITLED fires once).
        var isFirstTitle = state.Title.IsNullOrEmpty() && !summary.Title.IsNullOrEmpty();
        state = state with {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            EndEntryLid = summary.EndEntryLid,
            MessageCount = summary.MessageCount,
            AuthorIds = summary.AuthorIds.Count > 0 ? summary.AuthorIds : state.AuthorIds,
            IsExpandedByDefault = summary.IsExpandedByDefault,
            LastSummaryAt = Clocks.SystemClock.Now,
            Version = VersionGenerator.NextVersion(state.Version),
        };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        if (isFirstTitle)
            await EnqueueLiveNotification(
                state, ConversationNotificationPhase.Titled, $"Voice chat: {summary.Title}", cancellationToken)
                .ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task SetContextStart(ChatId chatId, long contextStartLid, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null || state.ContextStartLid > 0)
            return;

        state = state with {
            ContextStartLid = contextStartLid,
            Version = VersionGenerator.NextVersion(state.Version),
        };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    public virtual async Task FinalizeSession(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return;
        if (await IsSessionLive(chatId).ConfigureAwait(false))
            return; // someone is streaming again - keep the session live

        await CloseAndMaterialize(state, cancellationToken).ConfigureAwait(false);
    }

    // Voice-call ring lifecycle

    public virtual async Task<CallId> StartCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        string? sessionHash,
        string? clientId,
        CancellationToken cancellationToken)
    {
        // Ring each distinct invitee except the caller - once, and never the caller themselves.
        invitees = invitees.Where(id => id != callerAuthorId).Distinct().ToApiArray();
        // Decided before the claims, which carry it - and those are taken before the lock, since they
        // are RPCs to the users' own shards. The lock re-checks the decision.
        var joinedCallId = GetJoinableCallId(await SafeGetCall(chatId).ConfigureAwait(false), callerAuthorId);
        var callId = joinedCallId ?? NewCallId(chatId);
        // A claim the call then fails to justify is ended by the first CallsBackend.GetUserCall that
        // reads it. A one-invitee call names its peer in the claim, so the caller's screens don't have to
        // read the invites to know who they're calling; a group call has no single peer to name.
        var callerPeerId = invitees.Count == 1 ? invitees[0] : null;
        if (!await ClaimUserCall(chatId, callId, callerAuthorId, CallRole.Caller, CallPhase.Dialing,
                callerPeerId, hasVideo, sessionHash, clientId, cancellationToken).ConfigureAwait(false))
            throw StandardError.Constraint("You're already in a call.");

        var ringing = new List<AuthorId>();
        var busy = new List<AuthorId>();
        foreach (var invitee in invitees) {
            var isFree = await ClaimUserCall(chatId, callId, invitee, CallRole.Callee, CallPhase.Ringing,
                callerAuthorId, hasVideo, null, null, cancellationToken).ConfigureAwait(false);
            (isFree ? ringing : busy).Add(invitee);
        }

        bool isOvertaken;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            var isJoining = call?.Id == callId;
            // Another call took the chat since the claims were made: joining it now would leave them
            // naming a call that isn't, and replacing it would end a ring somebody is waiting on.
            // And a call that was there to join may be gone: its id must not name the one placed instead,
            // or the close still running for it would take this one's claims along.
            isOvertaken = !isJoining
                && (joinedCallId is not null || GetJoinableCallId(call, callerAuthorId) is not null
                    || IsUnresolvedDial(call));
            if (!isOvertaken)
                await WriteCall(call, isJoining).ConfigureAwait(false);
        }
        if (isOvertaken) {
            await EndUserCalls(
                    chatId, callId, ringing.Prepend(callerAuthorId), CallOutcome.None, CancellationToken.None)
                .ConfigureAwait(false);
            throw StandardError.Constraint(joinedCallId is not null
                ? "The call has just ended. Please try again."
                : "There's already a call in this chat.");
        }

        Log.LogInformation(
            "StartCall: call #{CallId} by author #{AuthorId}, ringing {RingingCount}, busy {BusyCount}",
            callId, callerAuthorId, ringing.Count, busy.Count);
        if (ringing.Count > 0) {
            await Services.Queues()
                .Enqueue(
                    new NotificationsBackend_NotifyCall(callId, callerAuthorId, ringing.ToApiArray(), hasVideo),
                    cancellationToken)
                .ConfigureAwait(false);
            _ = ScheduleRingTimeout(chatId);
        }
        // Nobody can answer: finalize now rather than let the caller dial into a call that will never ring.
        if (ringing.Count == 0 && busy.Count > 0)
            await CloseBusyCall(callId, cancellationToken).ConfigureAwait(false);
        return callId;

        // Caller must hold the change lock.
        async Task WriteCall(LiveCall? call, bool isJoining) {
            var now = Clocks.SystemClock.Now;
            // The chat's session, if it has one, is left alone: the call joins it only when answered.
            if (!isJoining) {
                call = new LiveCall {
                    Id = callId,
                    CallerId = callerAuthorId,
                    HasVideo = hasVideo,
                    StartedAt = now,
                    Version = VersionGenerator.NextVersion(),
                };
                await _calls.Set(chatId.Value, call, RingingCallTtl).ConfigureAwait(false);
                await _invites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
            }
            // Dialing status only while unanswered; inviting more people into a connected call isn't "calling".
            await SetCallState(chatId, call!.IsAnswered ? null : NewCallState(call, CallStatus.Dialing))
                .ConfigureAwait(false);
            foreach (var invitee in ringing)
                await _invites.Set(chatId.Value, invitee.Value,
                        new CallInvite {
                            InviteeId = invitee,
                            Status = CallInviteStatus.Ringing,
                            RingingAt = now,
                            CallId = callId,
                        },
                        RingTtl)
                    .ConfigureAwait(false);
            // An invitee already in another call is never rung: the invite is closed here, and they are
            // left out of the notification batch below, so no device of theirs is even pushed.
            foreach (var invitee in busy)
                await _invites.Set(chatId.Value, invitee.Value,
                        new CallInvite {
                            InviteeId = invitee,
                            Status = CallInviteStatus.Busy,
                            RingingAt = now,
                            RespondedAt = now,
                            Ack = RingAck.Busy,
                            AckAt = now,
                            CallId = callId,
                        },
                        RingTtl)
                    .ConfigureAwait(false);
            InvalidateCall(chatId);
        }
    }

    public virtual async Task AcceptCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        string? sessionHash,
        string? clientId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        var isReclaimed = await ClaimAnswer(
            chatId, inviteeAuthorId, sessionHash, clientId, callId, cancellationToken).ConfigureAwait(false);
        (bool IsAccepted, bool JustConnected) accepted;
        try {
            accepted = await AcceptInvite(chatId, inviteeAuthorId, callId, cancellationToken).ConfigureAwait(false);
        }
        catch when (isReclaimed) {
            // A late answer the call refused: to this callee it stays the ring they missed.
            await EndUserCall(chatId, callId, inviteeAuthorId, CallOutcome.NoAnswer, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        if (accepted.IsAccepted)
            await DismissRing(callId, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
        if (accepted.JustConnected)
            _ = ScheduleCallPartiesCheck(chatId, nameof(EnforceCallConnectGrace), CallConnectGrace);
    }

    public virtual async Task DeclineCall(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        var abandoned = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            if (IsAnotherCall(call, callId, inviteeAuthorId, nameof(DeclineCall)))
                return;

            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(DeclineCall),
                    invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
                return;

            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite! with { Status = CallInviteStatus.Declined, RespondedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            abandoned = await IsCallAbandoned(chatId, call).ConfigureAwait(false);
            // Gated on abandoned: a decline while another invitee still rings isn't the call's final story.
            if (abandoned)
                await RecomputeCallStatus(chatId, call, cancellationToken).ConfigureAwait(false);
            // Unlike CallStatus, the outcome is recorded on every decline: first-writer-wins already
            // covers a later accept or cancel rewriting the story.
            await SetOutcome(chatId, call, CallOutcome.Declined).ConfigureAwait(false);
            InvalidateCall(chatId);
        }
        await EndUserCall(chatId, callId, inviteeAuthorId, CallOutcome.Declined, cancellationToken)
            .ConfigureAwait(false);
        await DismissRing(callId, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
        if (abandoned)
            await EndCall(chatId, callId: callId).ConfigureAwait(false);
    }

    public virtual async Task ConfirmRing(
        ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken)
    {
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(ConfirmRing),
                    invite?.Status ?? CallInviteStatus.New, invite is { Status: CallInviteStatus.Ringing }))
                return;

            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite! with { Ack = ack, AckAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            InvalidateInvites(chatId);
        }
    }

    public virtual async Task CancelCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        // The caller hangs up. Unanswered, that cancels the call; answered, it is a party leaving it.
        var ringing = new List<AuthorId>();
        int? callPartiesLeft = null;
        var outcome = CallOutcome.Canceled;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            if (IsAnotherCall(call, callId, callerAuthorId, nameof(CancelCall)))
                return;

            // A call is its caller's to cancel; an invitee's way out is DeclineCall.
            if (call.CallerId != callerAuthorId) {
                Log.LogWarning(
                    "{SignalName} rejected for author #{AuthorId}: call #{CallId} was placed by #{CallerId}",
                    nameof(CancelCall), callerAuthorId, callId, call.CallerId);
                return;
            }

            // The client cancels while its slot still reads Dialing, which can outlast the answer by a
            // round trip: the caller is then leaving a connected call, as a hang-up from it would (#4984).
            if (call.IsAnswered) {
                outcome = CallOutcome.Ended;
                await _participants.Remove(chatId.Value, callerAuthorId.Value).ConfigureAwait(false);
                callPartiesLeft = await CountCallPartiesLeft(chatId, callerAuthorId, cancellationToken)
                    .ConfigureAwait(false);
                InvalidateListParticipants(chatId);
                InvalidateHasRecorder(chatId);
            }
            else {
                var now = Clocks.SystemClock.Now;
                foreach (var info in (await SafeGetInvites(chatId).ConfigureAwait(false)).Values) {
                    if (info is not { Status: CallInviteStatus.Ringing })
                        continue;

                    ringing.Add(info.InviteeId);
                    await _invites.Set(chatId.Value, info.InviteeId.Value,
                            info with { Status = CallInviteStatus.Missed, RespondedAt = now })
                        .ConfigureAwait(false);
                }
                // Hanging up myself needs no status, and it must beat a decline that just landed.
                await SetCallState(chatId, null).ConfigureAwait(false);
                await SetOutcome(chatId, call, CallOutcome.Canceled).ConfigureAwait(false);
            }
            InvalidateCall(chatId);
        }
        await EndUserCalls(chatId, callId, ringing.Prepend(callerAuthorId), outcome, cancellationToken)
            .ConfigureAwait(false);
        if (ringing.Count > 0)
            await DismissRing(callId, ringing, cancellationToken).ConfigureAwait(false);
        if (callPartiesLeft is { } partiesLeft)
            await OnCallPartyLeft(chatId, partiesLeft).ConfigureAwait(false);
        else
            await EndCall(chatId, callId: callId).ConfigureAwait(false);
    }

    // Legacy methods

    public Task LegacyOnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken)
        => OnStreamRegistered(chatId, authorId, entryLid, transcriptionOn, true, cancellationToken);

    // Protected/internal methods

    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<long?> GetConsolidatedVisibleStartLid(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // Protected on purpose: ConsolidationDelay applies only to methods computed locally, and an
        // RPC-visible one is served from a RemoteComputed on the caller's side instead.
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        return state is { SessionStartedAt: not null } ? state.EffectiveVisibleStartLid : null;
    }

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(ConversationContentComparer))]
    protected virtual async Task<Conversation?> GetConsolidatedLiveConversation(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // The comparer is required: ToConversation() rebuilds the card, and Conversation compares by reference.
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        return state is { SessionStartedAt: not null } ? state.ToConversation() : null;
    }

    [ComputeMethod(ConsolidationDelay = 0.2, ConsolidationComparer = typeof(ApiArrayComparer<AuthorId>))]
    protected virtual async Task<ApiArray<AuthorId>> GetConsolidatedParticipants(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var authorIds = await GetFreshParticipantIds(chatId).ConfigureAwait(false);
        if (authorIds.Count > 0)
            // Re-check so a stale (left) participant drops without an explicit off signal.
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return authorIds;
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    protected virtual async Task<bool> GetConsolidatedHasRecorder(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var hasRecorder = participants.Values.Any(p => IsFreshRecorder(p, cutoff));
        if (hasRecorder)
            // Re-check so a stale (crashed) recorder drops without an explicit off signal.
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return hasRecorder;
    }

    // Private methods

    private Task WakeSummaryFlow(ChatId chatId)
        // Runs the flow's final pass at once rather than on its next throttled resume, which is what
        // makes a closing block finalize promptly. The zero quantum keeps the Uuid unquantized: under
        // the flow's own 15s DelayQuanta this wake would share a Uuid with that slot's throttled
        // resume and be dropped as a duplicate - not delayed to it, dropped. FlowId is built by name
        // so the streaming backend needn't reference the Chat.Service flow type.
        => FlowHub.NewResumeEvent(new FlowId(LiveFlows.SummaryFlowName, chatId.Value))
            .WithDelay(TimeSpan.Zero, TimeSpan.Zero)
            .Schedule(CancellationToken.None);

    private Task WakeCallTailFlow(ConversationId conversationId)
        // The last utterance's entry can be created after this close, since it's created on the first
        // non-empty transcript - the flow grows the conversation over whatever lands and re-sizes it.
        => FlowHub.NewResumeEvent(new FlowId(LiveFlows.CallTailFlowName, conversationId.Value))
            .WithDelay(Clocks.SystemClock.Now + CallTailDelay, TimeSpan.Zero)
            .Schedule(CancellationToken.None);

    // Caller must hold the change lock. Null when the leaver is no party to an answered call.
    private async Task<int?> CountCallPartiesLeft(
        ChatId chatId, AuthorId leaverId, CancellationToken cancellationToken)
    {
        // Ambient sessions have no two-party invariant: solo dictation is legitimate, and someone else in
        // the session the call was placed into leaving it isn't the call's business.
        var call = await SafeGetCall(chatId).ConfigureAwait(false);
        if (call is not { IsAnswered: true })
            return null;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        if (!partyIds.Contains(leaverId))
            return null;

        var count = await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false);
        if (count >= MinCallParties(chatId) && await SafeGet(chatId).ConfigureAwait(false) is { IsCall: true } state
            && state.Host == leaverId)
            await ReassignHost(chatId, state, cancellationToken).ConfigureAwait(false);
        return count;
    }

    // Outside the change lock: EndCall takes it.
    private async Task OnCallPartyLeft(ChatId chatId, int partiesLeft)
    {
        // Below MinCallParties a call is over: what's left of it gets CallLeaveGrace to come back, and
        // none left closes now.
        if (partiesLeft == 0)
            await EndCall(chatId, mustRecheckParties: true).ConfigureAwait(false);
        else if (partiesLeft < MinCallParties(chatId))
            _ = ScheduleCallPartiesCheck(chatId, nameof(EnforceCallLeaveGrace), CallLeaveGrace);
    }

    private static int MinCallParties(ChatId chatId)
        // A peer call is its two parties, so either one leaving ends it. Elsewhere a call goes on while anyone
        // is in it and ends once the last one leaves: nobody stays on a call that ended under them unseen.
        => chatId.Kind == ChatKind.Peer ? 2 : 1;

    private async Task ReassignHost(ChatId chatId, LiveSessionState state, CancellationToken cancellationToken)
    {
        // Without this the host slot would keep pointing at someone who already left, and in a call
        // with no Owner present nobody could take it over.
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var candidateIds = participants.Keys
            .Select(x => AuthorId.TryParse(x, out var id) ? id : null)
            .SkipNullItems()
            .ToList();
        if (candidateIds.Count == 0)
            return;

        var ownerIds = (await RolesBackend
            .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Owner, cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet();
        var newHost = candidateIds.FirstOrDefault(ownerIds.Contains) ?? candidateIds[0];
        await _redisScope
            .Set(chatId.Value, state with {
                Host = newHost,
                Version = VersionGenerator.NextVersion(state.Version),
            })
            .ConfigureAwait(false);
        InvalidateState(chatId);
    }

    private Task ScheduleCallPartiesCheck(ChatId chatId, string checkName, TimeSpan grace)
        // Logged as the pair to EnforceCallParties' verdict: the two lines bracket the window a client has
        // to get (or get back) its presence up, which is what a slow accept or a presence dip loses.
        => BackgroundTask.Run(async () => {
            // The check is for the call the chat is in now: by the time it runs, that one may be over
            // and the next one connecting - with a grace of its own.
            var callId = (await SafeGetCall(chatId).ConfigureAwait(false))?.Id;
            Log.LogInformation(
                "{CheckName}: call #{CallId} - checking back in {Grace}",
                checkName, callId?.Value ?? chatId.Value, grace.ToShortString());
            await Task.Delay(grace).ConfigureAwait(false);
            await EnforceCallParties(chatId, checkName, grace, callId).ConfigureAwait(false);
        }, Log, $"{checkName} check failed for chat #{chatId}");

    private Task ScheduleRingTimeout(ChatId chatId)
        // GetCall's self-heal alone notices a lapsed ring only on its next period, up to SelfHealDelay late
        // (#4755). The margin keeps a timer that fires a hair early from finding the ring still fresh.
        => BackgroundTask.Run(async () => {
            await Task.Delay(RingTimeout + TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
            await ExpireRings(chatId).ConfigureAwait(false);
        }, Log, $"Ring timeout check failed for chat #{chatId}");

    private Task ScheduleAnswerGraceEnd(ChatId chatId)
        // A missed ring holds the call open for a late answer, and GetCall's self-heal that would close it
        // otherwise is SelfHealDelay away - the caller would keep dialing that much longer.
        => BackgroundTask.Run(async () => {
            await Task.Delay(AnswerGrace).ConfigureAwait(false);
            await ExpireRings(chatId).ConfigureAwait(false);
        }, Log, $"Answer-grace check failed for chat #{chatId}");

    // AcceptCall schedules this once, fire-and-forget, CallConnectGrace after the first answer.
    // Internal so a test can drive it directly, without a real wait - mirrors ExpireRings.
    internal Task EnforceCallConnectGrace(ChatId chatId)
        => EnforceCallParties(chatId, nameof(EnforceCallConnectGrace), CallConnectGrace);

    // SetParticipation schedules this, CallLeaveGrace after a leave that left the call one party.
    // Internal so a test can drive it directly, without a real wait.
    internal Task EnforceCallLeaveGrace(ChatId chatId)
        => EnforceCallParties(chatId, nameof(EnforceCallLeaveGrace), CallLeaveGrace);

    private async Task EnforceCallParties(ChatId chatId, string checkName, TimeSpan grace, CallId? callId = null)
    {
        try {
            var shouldClose = false;
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                var call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (callId is not null && call is not null && call.Id != callId)
                    Log.LogInformation(
                        "{CheckName}: call #{CallId} is over, chat #{ChatId} is in #{CurrentCallId} - nothing to check",
                        checkName, callId, chatId, call.Id);
                else if (call is { IsAnswered: true }) {
                    callId = call.Id;
                    var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
                    var partyCount = await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false);
                    if (partyCount < MinCallParties(chatId))
                        shouldClose = true;
                    else
                        await RecomputeCallStatus(chatId, call, CancellationToken.None).ConfigureAwait(false);

                    // The verdict this method exists for, and the one thing that tells a closed call
                    // apart from one a party left: a client registers its presence through its
                    // participation sync, so the count here is what the race actually turns on.
                    Log.LogInformation(
                        "{CheckName}: chat #{ChatId} - {PartyCount} call part(ies) present after {Grace}, {Verdict}",
                        checkName, chatId, partyCount, grace.ToShortString(),
                        shouldClose ? "closing the call" : "keeping it");
                }
                else
                    Log.LogInformation(
                        "{CheckName}: chat #{ChatId} - no answered call to check", checkName, chatId);
            }
            if (shouldClose)
                await EndCall(chatId, mustRecheckParties: true, callId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "{CheckName} failed for chat #{ChatId}", checkName, chatId);
        }
    }

    // Drives CallInviteStatus.Active/Ended (and the caller-side equivalent on CallState) from genuine
    // presence, by the same freshness rule Ambient sessions already use - never from a
    // raw participant count, and never written inline from SetParticipation, which has no way to detect
    // a silent crash. Internal so a test can drive it directly, without a real self-heal wait.
    internal async Task SyncCallParticipantActivity(
        ChatId chatId, LiveCall call, CancellationToken cancellationToken)
    {
        if (!call.IsAnswered)
            return;

        try {
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
                // Re-read under the lock instead of trusting the pre-lock `call` parameter: a
                // concurrent CancelCall/DeclineCall/EndCall may have already torn the call down, and
                // RecomputeCallStatus below must not resurrect a status for a call that no longer exists.
                var freshCall = await SafeGetCall(chatId).ConfigureAwait(false);
                if (freshCall is not { IsAnswered: true } || freshCall.Id != call.Id)
                    return;

                // Read once the lock is held (mirrors EnforceCallConnectGrace), not before: GetCall's
                // self-heal fires a new Sync on every tick, so several can be queued on this same chat's
                // lock at once - reading freshness before the lock would let a stale snapshot from an
                // earlier, slower tick win the write race and revert a just-applied Ended back to Active.
                var freshAuthorIds = (await GetFreshParticipantIds(chatId).ConfigureAwait(false)).ToHashSet();
                var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
                var callerId = freshCall.CallerId;
                // Gate the "become Active" transitions on how many of THIS call's own roster (the
                // caller plus its own invitees) are fresh - not the chat-wide fresh count, which an
                // unrelated bystander (an already-latched Ambient session, or someone else's unrelated
                // stream in the same chat) could satisfy on its own and flip CallerActiveAt before
                // anyone in this call has actually answered.
                var callRosterFreshCount = (freshAuthorIds.Contains(callerId) ? 1 : 0)
                    + invites.Values.Count(i => i is not null && freshAuthorIds.Contains(i.InviteeId));

                var changed = await SyncCallerActivity(
                        chatId, freshCall, callerId, freshAuthorIds, callRosterFreshCount)
                    .ConfigureAwait(false);
                changed |= await SyncInviteeActivity(chatId, invites, freshAuthorIds, callRosterFreshCount)
                    .ConfigureAwait(false);
                if (changed)
                    await RecomputeCallStatus(chatId, freshCall, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SyncCallParticipantActivity failed for chat #{ChatId}", chatId);
        }
    }

    // Caller must hold the change lock.
    private async Task<bool> SyncCallerActivity(
        ChatId chatId,
        LiveCall call,
        AuthorId callerId,
        HashSet<AuthorId> freshAuthorIds,
        int callRosterFreshCount)
    {
        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        var isFresh = freshAuthorIds.Contains(callerId);
        var wasActive = callState is { CallerActiveAt: not null, CallerEndedAt: null };
        // CallerEndedAt is a ratchet too, same as the invitee side's Declined/Missed/Ended check below -
        // once the caller has been marked ended, a later fresh reading must not flip them back to Active.
        var isTerminal = callState is { CanceledAt: not null } or { CallerEndedAt: not null };

        // Gated on >= 2 fresh participants from THIS CALL's own roster (see callRosterFreshCount above),
        // not the caller alone: the caller is fresh from the moment the call is answered (AcceptInvite
        // registers them), so without this Derive would see a lone active party and report the whole
        // call Ended before the invitee even connected.
        if (isFresh && callRosterFreshCount >= 2 && !wasActive && !isTerminal) {
            await SetCallState(chatId, (callState ?? NewCallState(call, CallStatus.Dialing)) with {
                CallerActiveAt = callState?.CallerActiveAt ?? Clocks.SystemClock.Now,
            }).ConfigureAwait(false);
            return true;
        }
        if (!isFresh && wasActive) {
            await SetCallState(chatId, callState! with { CallerEndedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            return true;
        }
        if (isFresh && isTerminal)
            Log.LogWarning(
                "SyncCallParticipantActivity: caller #{AuthorId} of chat #{ChatId} reactivated after "
                + "a terminal status - ignored", callerId, chatId);
        return false;
    }

    // Caller must hold the change lock.
    private async Task<bool> SyncInviteeActivity(
        ChatId chatId,
        Dictionary<string, CallInvite?> invites,
        HashSet<AuthorId> freshAuthorIds,
        int callRosterFreshCount)
    {
        var changed = false;
        foreach (var (authorIdValue, invite) in invites) {
            if (invite is null || !AuthorId.TryParse(authorIdValue, out var authorId))
                continue;

            var isFresh = freshAuthorIds.Contains(authorId);
            var now = Clocks.SystemClock.Now;
            switch (invite.Status) {
                // The roster count keeps a lone fresh invitee from turning Active on its own; only an answered
                // call syncs, so one present in the session the call rang into isn't answered for them.
                case CallInviteStatus.Ringing when isFresh && callRosterFreshCount >= 2:
                case CallInviteStatus.Accepted when isFresh && callRosterFreshCount >= 2:
                    await _invites.Set(chatId.Value, authorIdValue,
                            invite with { Status = CallInviteStatus.Active, ActiveAt = now })
                        .ConfigureAwait(false);
                    changed = true;
                    break;
                case CallInviteStatus.Active when !isFresh:
                    await _invites.Set(chatId.Value, authorIdValue,
                            invite with { Status = CallInviteStatus.Ended, EndedAt = now })
                        .ConfigureAwait(false);
                    changed = true;
                    break;
                case CallInviteStatus.Declined or CallInviteStatus.Missed or CallInviteStatus.Ended when isFresh:
                    Log.LogWarning(
                        "SyncCallParticipantActivity: invitee #{AuthorId} of chat #{ChatId} reactivated "
                        + "after status {Status} - ignored", authorId, chatId, invite.Status);
                    break;
            }
        }
        if (changed)
            InvalidateInvites(chatId);
        return changed;
    }

    // The user's call lives on the user's shard, so each of these is an RPC - never call them
    // while holding a chat's change lock.
    private async Task<bool> ClaimUserCall(
        ChatId chatId,
        CallId callId,
        AuthorId authorId,
        CallRole role,
        CallPhase phase,
        AuthorId? peerId,
        bool hasVideo,
        string? sessionHash,
        string? clientId,
        CancellationToken cancellationToken)
    {
        if (await GetUserId(chatId, authorId, cancellationToken).ConfigureAwait(false) is not { } userId)
            return true; // No user behind this author: nothing to arbitrate, so never block the call

        var call = new UserCallClaim {
            ChatId = chatId,
            AuthorId = authorId,
            Role = role,
            Phase = phase,
            PeerId = peerId,
            HasVideo = hasVideo,
            SessionHash = sessionHash,
            ClientId = clientId,
            CallId = callId,
        };
        return await CallsBackend.TryClaim(userId, call, cancellationToken).ConfigureAwait(false);
    }

    // Reports whether this answer was taken, and whether it was the first one; neither for an answer
    // that was already given.
    private async Task<(bool IsAccepted, bool JustConnected)> AcceptInvite(
        ChatId chatId, AuthorId inviteeAuthorId, CallId callId, CancellationToken cancellationToken)
    {
        var justConnected = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            // The ring this answer was tapped on is gone, and what rings here now is another call.
            if (IsAnotherCall(call, callId, inviteeAuthorId, nameof(AcceptCall)))
                throw StandardError.Constraint("There's no ring left to accept in this chat.");

            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            // Answering a call that is already ours is the same answer, not an error: an RPC resend
            // after a reconnect, a second tap, and SyncInviteeActivity's own Ringing -> Active promotion
            // all land here. The caller tears its call down on a throw, so this has to stay idempotent.
            if (invite is { Status: CallInviteStatus.Accepted or CallInviteStatus.Active })
                return (false, false);

            var now = Clocks.SystemClock.Now;
            var isLate = IsAnswerableMissedRing(invite, call, now);
            // Runs under the change lock, so it's the answer - a client-side check races the call it
            // reads. And a call that already has an outcome is over, whatever its invites still say.
            var isValid = (invite is { Status: CallInviteStatus.Ringing } || isLate)
                && call.Outcome is CallOutcome.None or CallOutcome.Declined;
            if (!EnsureValidTransition(chatId, inviteeAuthorId, nameof(AcceptCall),
                    invite?.Status ?? CallInviteStatus.New, isValid))
                throw StandardError.Constraint("There's no ring left to accept in this chat.");

            if (isLate)
                Log.LogInformation(
                    "AcceptCall: call #{CallId}, author #{AuthorId} answered a missed ring {Delay} after it began",
                    callId, inviteeAuthorId, (now - invite!.RingingAt).ToShortString());
            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite! with { Status = CallInviteStatus.Accepted, RespondedAt = now })
                .ConfigureAwait(false);

            if (!call.IsAnswered) {
                call = call with { AnsweredAt = now, Version = VersionGenerator.NextVersion(call.Version) };
                await _calls.Set(chatId.Value, call).ConfigureAwait(false);
                await JoinSession(call, inviteeAuthorId, cancellationToken).ConfigureAwait(false);
                // The answer is the caller's "accepted" moment - a brief confirmation before this fades.
                await RecomputeCallStatus(chatId, call, cancellationToken).ConfigureAwait(false);
                justConnected = true;
            }
            // Both claims turn Active on their own: CallsBackend reads the phase off the invite and the
            // call this has just written.
            InvalidateCall(chatId);
            return (true, justConnected);
        }
    }

    // Caller must hold the change lock. The first answer gives the call its session: a new one the call
    // started, or the one already in the chat, which the call then leaves as it was found.
    private async Task JoinSession(LiveCall call, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        var chatId = call.ChatId;
        if (await SafeGet(chatId).ConfigureAwait(false) is null) {
            var lidRangeEnd = (await ChatsBackend
                .GetLidRange(chatId, false, cancellationToken)
                .ConfigureAwait(false)).End;
            var state = new LiveSessionState {
                ChatId = chatId,
                StartEntryLid = lidRangeEnd,
                EndEntryLid = lidRangeEnd,
                // The ring, not the answer: the call card's span is set from SessionStartedAt at its close.
                StartedAt = call.StartedAt,
                SessionStartedAt = call.AnsweredAt,
                VisibleStartLid = lidRangeEnd,
                AuthorIds = [call.CallerId, inviteeAuthorId],
                Host = call.CallerId,
                CallerId = call.CallerId,
                Kind = LiveSessionKind.Call,
                Version = VersionGenerator.NextVersion(),
            };
            await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
            // The caller is present from the answer; the invitee only once their client listens or records,
            // which is what lets EnforceCallConnectGrace tell "accepted" from "connected".
            await EnsureParticipant(chatId, call.CallerId).ConfigureAwait(false);
            InvalidateState(chatId);
        }
    }

    // Taken before the answer is written. A ring's claim names no client, so every client of the callee
    // sees it; the answer makes it the answering client's alone (#4929). A missed ring's claim is gone
    // (ExpireRings released it), so a late answer takes it anew - and a callee who took another call
    // meanwhile can't also connect this one. Reports whether it was taken anew, for the caller to
    // release it if the answer is refused after all.
    private async Task<bool> ClaimAnswer(
        ChatId chatId,
        AuthorId inviteeAuthorId,
        string? sessionHash,
        string? clientId,
        CallId callId,
        CancellationToken cancellationToken)
    {
        var call = await SafeGetCall(chatId).ConfigureAwait(false);
        // Refused for good under the change lock, in AcceptInvite; here it only must not claim anything.
        if (call?.Id != callId)
            return false;

        var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
        var isMissed = invite is { Status: CallInviteStatus.Missed };
        var isRingToOwn = invite is { Status: CallInviteStatus.Ringing } && sessionHash is not null;
        if (!isMissed && !isRingToOwn)
            return false;

        if (!await ClaimUserCall(chatId, callId, inviteeAuthorId, CallRole.Callee, CallPhase.Active,
                call.CallerId, call.HasVideo, sessionHash, clientId, cancellationToken)
                .ConfigureAwait(false))
            throw StandardError.Constraint("You're already in another call.");

        return isMissed;
    }

    private async Task EndUserCall(
        ChatId chatId,
        CallId? callId,
        AuthorId authorId,
        CallOutcome outcome,
        CancellationToken cancellationToken)
    {
        // Frees the user, and tells the client running the call that it is over - the one signal that
        // client stops the call's media on.
        // Only a call takes claims, and every call has an id: a session with none is an ambient one.
        if (callId is null)
            return;

        if (await GetUserId(chatId, authorId, cancellationToken).ConfigureAwait(false) is { } userId)
            await CallsBackend.EndCall(userId, callId, outcome, cancellationToken).ConfigureAwait(false);
    }

    private async Task EndUserCalls(
        ChatId chatId,
        CallId? callId,
        IEnumerable<AuthorId> authorIds,
        CallOutcome outcome,
        CancellationToken cancellationToken)
    {
        foreach (var authorId in authorIds)
            await EndUserCall(chatId, callId, authorId, outcome, cancellationToken).ConfigureAwait(false);
    }

    // A request is for the call it names only: by the time it arrives the chat may be in the next one,
    // or in none.
    private bool IsAnotherCall(
        [NotNullWhen(false)] LiveCall? call,
        CallId callId,
        AuthorId authorId,
        string signalName)
    {
        if (call?.Id == callId)
            return false;

        Log.LogWarning(
            "{SignalName} rejected for author #{AuthorId}: it names call #{CallId}, the chat is in #{CurrentCallId}",
            signalName, authorId, callId, call?.Id.Value ?? "none");
        return true;
    }

    // The call a StartCall joins instead of placing a new one: an answered one - or the caller's own,
    // still dialing: an RPC resent after a reconnect must find the call it placed.
    private static CallId? GetJoinableCallId(LiveCall? call, AuthorId callerAuthorId)
    {
        var isOwnDial = IsUnresolvedDial(call) && call!.CallerId == callerAuthorId;
        return call is { IsAnswered: true } || isOwnDial ? call!.Id : null;
    }

    private static bool IsUnresolvedDial(LiveCall? call)
        => call is { IsResolved: false };

    private CallId NewCallId(ChatId chatId)
    {
        // The start time in ms: it reads in a log, and sorts. Bumped past the last one issued here, so
        // two calls never share it.
        var nowMs = (long)Clocks.SystemClock.Now.EpochOffset.TotalMilliseconds;
        long last, next;
        do {
            last = Interlocked.Read(ref _lastCallLocalId);
            next = Math.Max(nowMs, last + 1);
        } while (Interlocked.CompareExchange(ref _lastCallLocalId, next, last) != last);
        return CallId.New(chatId, next.ToString());
    }

    private async Task<UserId?> GetUserId(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        // A guest's id is a user id all the same: they can be in a call, so they can be busy in one.
        return author?.UserId is { } userId && !userId.Value.IsNullOrEmpty() ? userId : null;
    }

    // Every invitee was busy, so this call can never ring: tell the caller and close it, instead of
    // leaving them dialing until the ring times out.
    private async Task CloseBusyCall(CallId callId, CancellationToken cancellationToken)
    {
        var chatId = callId.ChatId;
        AuthorId callerId;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var call = await SafeGetCall(chatId).ConfigureAwait(false);
            if (call is not { IsAnswered: false } || call.Id != callId)
                return;

            callerId = call.CallerId;
            await SetCallState(chatId, NewCallState(call, CallStatus.Busy)).ConfigureAwait(false);
            await SetOutcome(chatId, call, CallOutcome.Busy).ConfigureAwait(false);
        }

        await EndUserCall(chatId, callId, callerId, CallOutcome.Busy, cancellationToken).ConfigureAwait(false);
        await EndCall(chatId, callId: callId).ConfigureAwait(false);
    }

    private async Task<LiveSessionState?> SafeGet(ChatId chatId)
    {
        try {
            return await _redisScope.Get(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read live conversation from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task<LiveCall?> SafeGetCall(ChatId chatId)
    {
        try {
            return await _calls.Get(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read the call from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task<ParticipationInfo?> SafeGetParticipant(ChatId chatId, AuthorId authorId)
    {
        try {
            return await _participants.Get(chatId.Value, authorId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read participants from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task EnsureParticipant(ChatId chatId, AuthorId authorId)
    {
        var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        // Preserve the client's real kind — a trailing utterance must not flip a now-listening author back to Record.
        var info = existing is null
            ? new ParticipationInfo(ParticipationKind.Record, now, JoinedAt: now)
            : existing with { RegisteredAt = now };
        await _participants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
        InvalidateListParticipants(chatId);
        InvalidateHasRecorder(chatId);
    }

    private async Task<Dictionary<string, ParticipationInfo?>> SafeGetHashMap(ChatId chatId)
    {
        try {
            return await _participants.GetHashMap(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read participants from Redis for chat #{ChatId}", chatId);
            return [];
        }
    }

    private async Task<CallState?> SafeGetCallState(ChatId chatId)
    {
        try {
            return await _callStates.Get(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read call state from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task SetCallState(ChatId chatId, CallState? callState)
    {
        try {
            if (callState is null)
                await _callStates.Remove(chatId.Value).ConfigureAwait(false);
            else
                await _callStates.Set(chatId.Value, callState, CallStateTtl(callState.Status)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to write call state to Redis for chat #{ChatId}", chatId);
            return;
        }

        using (Invalidation.Begin())
            _ = GetCallState(chatId, default);
    }

    // Internal (rather than private) to be accessible from tests: every real caller of this method also
    // unconditionally closes the call in the same operation once its outcome is the abandoning one, so the
    // guard below can't be pinned by observing Redis state through the close - it has to be called directly.
    internal async Task SetOutcome(ChatId chatId, LiveCall call, CallOutcome outcome)
    {
        // First writer wins: the earliest terminal response decides the outcome, and nothing later
        // may overwrite it - including a decline outranking another invitee's ring later timing out
        // into NoAnswer, and any outcome at all once the call was answered. Callers hold _changeLocks,
        // so this read and write are atomic.
        if (call.IsResolved)
            return;

        await _calls.Set(chatId.Value, call with {
            Outcome = outcome,
            Version = VersionGenerator.NextVersion(call.Version),
        }, RingingCallTtl).ConfigureAwait(false);
        InvalidateCall(chatId);
    }

    private CallState NewCallState(LiveCall call, CallStatus status, CallState? previous = null)
        => new() {
            CallerId = call.CallerId,
            Status = status,
            ChangedAt = Clocks.SystemClock.Now,
            CallerActiveAt = previous?.CallerActiveAt,
            CallerEndedAt = previous?.CallerEndedAt,
            CanceledAt = previous?.CanceledAt,
            CallId = call.Id,
        };

    internal static CallStatus Derive(CallState? callState, IReadOnlyCollection<CallInvite?> invites)
    {
        var everActive = callState?.CallerActiveAt is not null
            || invites.Any(i => i?.ActiveAt is not null);
        var activeCount = (callState?.CallerActiveAt is not null && callState.CallerEndedAt is null
                ? 1
                : 0)
            + invites.Count(i => i is { Status: CallInviteStatus.Active });

        if (activeCount >= 2)
            return CallStatus.Active;
        if (everActive)
            return CallStatus.Ended;
        if (callState?.CanceledAt is not null)
            return CallStatus.Canceled;
        if (invites.Any(i => i is { Status: CallInviteStatus.Accepted }))
            return CallStatus.Connecting;
        if (invites.Any(i => i is { Status: CallInviteStatus.Declined }))
            return CallStatus.Declined;
        if (invites.Count > 0 && invites.All(i => i is { Status: CallInviteStatus.Missed }))
            return CallStatus.NoAnswer;
        return CallStatus.Dialing;
    }

    private async Task RecomputeCallStatus(
        ChatId chatId, LiveCall call, CancellationToken cancellationToken)
    {
        var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
        var invites = (await SafeGetInvites(chatId).ConfigureAwait(false)).Values;
        var status = Derive(callState, invites);
        if (callState is null && status == CallStatus.Dialing)
            return;

        await SetCallState(chatId, NewCallState(call, status, callState)).ConfigureAwait(false);
    }

    private bool EnsureValidTransition<TStatus>(
        ChatId chatId, AuthorId authorId, string signalName,
        TStatus currentStatus, bool isValid)
        where TStatus : Enum
    {
        if (isValid)
            return true;

        Log.LogWarning(
            "{SignalName} rejected for chat #{ChatId}, author #{AuthorId}: not valid from status {CurrentStatus}",
            signalName, chatId, authorId, currentStatus);
        return false;
    }

    private static TimeSpan CallStateTtl(CallStatus status)
        => status is CallStatus.Dialing or CallStatus.Connecting or CallStatus.Active
            ? DialingStateTtl
            : ResolvedStateTtl;

    private async Task<CallInvite?> SafeGetInvite(ChatId chatId, AuthorId inviteeAuthorId)
    {
        try {
            return await _invites.Get(chatId.Value, inviteeAuthorId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read call invites from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task<Dictionary<string, CallInvite?>> SafeGetInvites(ChatId chatId)
    {
        try {
            return await _invites.GetHashMap(chatId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read call invites from Redis for chat #{ChatId}", chatId);
            return [];
        }
    }

    private async Task<bool> HasStaleRinging(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - RingTimeout;
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing } && i.RingingAt <= cutoff);
    }

    private async Task<bool> HasFreshRing(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - RingTimeout;
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing } && i.RingingAt > cutoff);
    }

    private static bool IsAnswerableMissedRing(CallInvite? invite, LiveCall call, Moment now)
        // A cancel marks the rings Missed too, and a resolved call has nobody left to answer.
        => IsInAnswerGrace(invite, now)
            && call is { IsAnswered: false, Outcome: CallOutcome.None or CallOutcome.Declined };

    private static bool IsInAnswerGrace(CallInvite? invite, Moment now)
        => invite is { Status: CallInviteStatus.Missed } && now - invite.RingingAt < RingTimeout + AnswerGrace;

    private Task DismissRing(CallId callId, IReadOnlyList<AuthorId> invitees, CancellationToken cancellationToken)
        => Services.Queues().Enqueue(new NotificationsBackend_CancelCall(callId, invitees), cancellationToken);

    // An unanswered invitee rang past RingTimeout: mark Missed and stop the ring (the caller still
    // sees "missed" and can hang up); the call closes as NoAnswer only once AnswerGrace is over too.
    // Fired by the ring timer and by GetCall's self-heal.
    // A dialing call is finalized here even when no invite is left to expire — a ring can vanish via
    // its RingTtl before this catches it, and the call must still reach an outcome rather than linger.
    // Internal rather than private so a test can drive it directly, without a real ring timeout.
    internal async Task ExpireRings(ChatId chatId)
    {
        try {
            LiveCall? call;
            var expired = new List<AuthorId>();
            var wasDialing = false;
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (call is null)
                    return;

                wasDialing = !call.IsAnswered;
                var now = Clocks.SystemClock.Now;
                var cutoff = now - RingTimeout;
                foreach (var info in (await SafeGetInvites(chatId).ConfigureAwait(false)).Values) {
                    if (info is not { Status: CallInviteStatus.Ringing } || info.RingingAt > cutoff)
                        continue;

                    expired.Add(info.InviteeId);
                    await _invites.Set(chatId.Value, info.InviteeId.Value,
                            info with { Status = CallInviteStatus.Missed, RespondedAt = now })
                        .ConfigureAwait(false);
                }
                if (expired.Count > 0)
                    InvalidateCall(chatId);
            }
            await EndUserCalls(chatId, call.Id, expired, CallOutcome.NoAnswer, CancellationToken.None)
                .ConfigureAwait(false);
            if (expired.Count > 0)
                await DismissRing(call.Id, expired, CancellationToken.None).ConfigureAwait(false);
            if (expired.Count > 0)
                _ = ScheduleAnswerGraceEnd(chatId);
            if ((expired.Count > 0 || wasDialing) && await IsCallAbandoned(chatId, call).ConfigureAwait(false)) {
                // The lock is retaken here because the outcome must be decided from a fresh read:
                // IsCallAbandoned above reads Redis without the lock, so it can catch AcceptCall's own
                // locked section mid-flight and come back true for a call answered a moment later.
                // shouldClose is decided from this same fresh, locked read, so a call that got there
                // before us keeps running instead of being torn down right after the client sees it connect.
                var shouldClose = false;
                using (Computed.BeginIsolation())
                using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                    // Only a still-dialing call gets a "no answer" - a connected one that emptied out just closes.
                    var freshCall = await SafeGetCall(chatId).ConfigureAwait(false);
                    if (freshCall is { IsAnswered: false } current && current.Id == call.Id
                        && await SafeGetCallState(chatId).ConfigureAwait(false)
                            is null or { Status: CallStatus.Dialing }) {
                        shouldClose = true;
                        await RecomputeCallStatus(chatId, current, CancellationToken.None).ConfigureAwait(false);
                        // Derive falls back to Dialing when there are no invite facts to work with
                        // (e.g. a zero-invitee StartCall, or every invite already vanished via its own
                        // RingTtl) - this is the call being closed as abandoned, so that generic
                        // fallback must not leave CallState stuck at Dialing past the call's own close.
                        var recomputed = await SafeGetCallState(chatId).ConfigureAwait(false);
                        if (recomputed is { Status: CallStatus.Dialing })
                            await SetCallState(chatId, NewCallState(current, CallStatus.NoAnswer, recomputed))
                                .ConfigureAwait(false);
                        await SetOutcome(chatId, current, CallOutcome.NoAnswer).ConfigureAwait(false);
                    }
                    else if (freshCall is not null)
                        // The unlocked check above read this call as abandoned, but it wasn't - flag it
                        // so a near-miss here is visible if this class of race ever fires for real.
                        Log.LogWarning(
                            "ExpireRings: abandon check for chat #{ChatId} was stale - "
                            + "call #{CallId} is already answered or replaced, not closing",
                            chatId, freshCall.Id);
                }
                // Outside the lock: EndCall takes the same non-reentrant lock.
                if (shouldClose)
                    await EndCall(chatId, callId: call.Id).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "ExpireRings failed for chat #{ChatId}", chatId);
        }
    }

    private static bool IsFreshRecorder(ParticipationInfo? info, Moment cutoff)
        => info is { Kind: ParticipationKind.Record } && info.RegisteredAt >= cutoff;

    private static bool IsFreshParticipant(ParticipationInfo? info, Moment cutoff)
        => info is not null && info.RegisteredAt >= cutoff;

    private async Task<ApiArray<AuthorId>> GetFreshParticipantIds(ChatId chatId)
    {
        // For decisions taken under the change lock: GetConsolidatedParticipants keeps serving the old
        // roster for ConsolidationDelay after a change, so a hang-up read through it still counts two.
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants
            .Where(kv => IsFreshParticipant(kv.Value, cutoff))
            .Select(kv => AuthorId.TryParse(kv.Key, out var id) ? id : null)
            .SkipNullItems()
            .ToApiArray();
    }

    private async Task<bool> HasParticipant(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants.Values.Any(p => IsFreshParticipant(p, cutoff));
    }

    private async Task<bool> HasFreshRecorder(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants.Values.Any(p => IsFreshRecorder(p, cutoff));
    }

    // The session is live only while someone is streaming - recording audio or video (a Record
    // participant; OnStreamRegistered registers every streamer as one). Pure listeners/watchers
    // don't keep it alive. A call answered in it is the exception: it ends with a hang-up, not with
    // silence, so its own parties present - listening counts - hold the session too.
    private async Task<bool> IsSessionLive(ChatId chatId)
    {
        if (await HasFreshRecorder(chatId).ConfigureAwait(false))
            return true;
        if (await SafeGetCall(chatId).ConfigureAwait(false) is not { IsAnswered: true } call)
            return false;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        return await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) >= MinCallParties(chatId);
    }

    // As IsSessionLive's call rule, but it also holds for CallConnectGrace after the answer, before the one
    // who answered has had the time to connect: GetCall's self-heal ends a call this reports dead.
    private async Task<bool> IsCallAlive(ChatId chatId, LiveCall call)
    {
        if (Clocks.SystemClock.Now - call.AnsweredAt < CallConnectGrace)
            return true;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        return await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) >= MinCallParties(chatId);
    }

    // The caller and whoever answered: someone else present in the session the call rang into isn't one.
    private async Task<HashSet<AuthorId>> GetCallPartyIds(ChatId chatId, LiveCall call)
    {
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return invites.Values
            .Where(i => i is { Status: CallInviteStatus.Accepted or CallInviteStatus.Active })
            .Select(i => i!.InviteeId)
            .Append(call.CallerId)
            .ToHashSet();
    }

    private async Task<int> CountFreshCallParties(ChatId chatId, HashSet<AuthorId> partyIds)
    {
        var freshIds = await GetFreshParticipantIds(chatId).ConfigureAwait(false);
        return freshIds.Count(partyIds.Contains);
    }

    private async Task<bool> IsCallAbandoned(ChatId chatId, LiveCall call)
    {
        // No invite is still ringing or answerable late, and the call has too few present parties to go on:
        // nobody else can join it any more, so it's abandoned - the whole thing should close.
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing } || IsInAnswerGrace(i, now)))
            return false;
        if (!call.IsAnswered)
            return true;

        var partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
        return await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) < MinCallParties(chatId);
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    // Returns true iff this call just transitioned the session into IsClosing.
    private async Task<bool> EvaluateLiveness(ChatId chatId)
    {
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return false;

        // A streamer (recording audio or video) keeps the session alive; it closes once nobody is
        // streaming, even if listeners/watchers remain - except in a connected call, see IsSessionLive.
        var isActive = await IsSessionLive(chatId).ConfigureAwait(false);
        if (isActive == !state.IsClosing)
            return false; // already active+open or inactive+closing

        state = isActive
            ? state with {
                IsClosing = false,
                ClosingAt = null,
                Version = VersionGenerator.NextVersion(state.Version),
            }
            : state with {
                IsClosing = true,
                ClosingAt = Clocks.SystemClock.Now,
                Version = VersionGenerator.NextVersion(state.Version),
            };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
        return !isActive;
    }

    private async Task StartClosingGrace(ChatId chatId)
    {
        bool startedClosing;
        try {
            using var _ = Computed.BeginIsolation();
            using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
            startedClosing = await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartClosingGrace failed for chat #{ChatId}", chatId);
            return;
        }
        // Outside the lock: wake the summary flow so it finalizes the just-closing session at once.
        if (startedClosing)
            await WakeSummaryFlow(chatId).ConfigureAwait(false);
    }

    // Immediate close: the last participant left explicitly, so wind the call down now rather than
    // marking it closing and waiting out the grace. Re-checks under no lock that nobody rejoined first.
    private async Task CloseNow(ChatId chatId)
    {
        try {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is null)
                return;
            if (await IsSessionLive(chatId).ConfigureAwait(false))
                return; // someone is (still) streaming - not empty after all
            if (state is { TranscriptionOn: true, SessionStartedAt: not null, Kind: LiveSessionKind.Ambient }) {
                // Hand the close to LiveConversationSummaryFlow: it runs the final summary pass, decides the
                // tier, materializes, then calls FinalizeSession. StartClosingGrace marks IsClosing; the 90s
                // SelfClose stays the backstop if the flow never finalizes.
                await StartClosingGrace(chatId).ConfigureAwait(false);
                return;
            }

            await CloseAndMaterialize(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CloseNow failed for chat #{ChatId}", chatId);
        }
    }

    // Call teardown, the one funnel every end of a call reaches. Unlike CloseNow it doesn't require anyone
    // to have left - a call with a single party is already over. mustRecheckParties is for an end decided
    // by headcount: it counts again under the lock, and a party back by then keeps the call. callId names
    // the call the end was decided for: the next call to the chat is not its to end.
    private async Task EndCall(ChatId chatId, bool mustRecheckParties = false, CallId? callId = null)
    {
        try {
            LiveCall? call;
            List<AuthorId> inviteeIds;
            HashSet<AuthorId> partyIds;
            // Dropping the call's key is the atomic claim that picks one closer out of the several that can
            // decide a call is over at once. It shares _changeLocks with every write of the key - outside it
            // a straddling read-modify-write puts the key back and the call is recorded twice - and the call
            // is re-read, as the callers' snapshots predate it.
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                call = await SafeGetCall(chatId).ConfigureAwait(false);
                if (call is null || (callId is not null && call.Id != callId))
                    return;

                partyIds = await GetCallPartyIds(chatId, call).ConfigureAwait(false);
                if (mustRecheckParties && call.IsAnswered
                    && await CountFreshCallParties(chatId, partyIds).ConfigureAwait(false) >= MinCallParties(chatId))
                    return;

                inviteeIds = (await SafeGetInvites(chatId).ConfigureAwait(false))
                    .Values.Where(i => i is not null).Select(i => i!.InviteeId).ToList();
                if (!await _calls.Remove(chatId.Value).ConfigureAwait(false))
                    return;
            }

            var endedAt = Clocks.SystemClock.Now;
            // An answered call is Ended whichever button ended it - CancelCall is also how a caller hangs
            // up - so the outcome recorded during the ring only decides a call that was never answered.
            var outcome = call.IsAnswered ? CallOutcome.Ended : call.Outcome;
            try {
                // Everything below runs with CancellationToken.None for the same reason the teardown in the
                // finally does: the claim above is once-or-never, so a token revoked mid-way would leave the
                // call with no trace at all, and nothing retries it.
                if (inviteeIds.Count > 0)
                    await DismissRing(call.Id, inviteeIds, CancellationToken.None).ConfigureAwait(false);
                await EndUserCalls(
                        chatId, call.Id, inviteeIds.Prepend(call.CallerId).Distinct(), outcome, CancellationToken.None)
                    .ConfigureAwait(false);
                if (chatId.Kind == ChatKind.Peer) {
                    // A call that rang into an ongoing session has no card of its own to gather its last words
                    // in, so its entry, visible there, has to come after them - see WriteCallEntryAfterTail.
                    var isInOngoingSession = call.IsAnswered
                        && await SafeGet(chatId).ConfigureAwait(false) is { IsCall: false };
                    if (isInOngoingSession)
                        _ = WriteCallEntryAfterTail(call, inviteeIds, partyIds, outcome, endedAt);
                    else
                        await WriteCallEntry(call, inviteeIds, outcome, endedAt, CancellationToken.None)
                            .ConfigureAwait(false);
                }
            }
            finally {
                // Having won the claim, this is the call's only closer: nothing retries it, so a failed ring
                // dismissal or entry must not also cost the teardown and the invalidation that tells clients
                // the call is over.
                try {
                    await CleanUpAfterCall(chatId, call.Id).ConfigureAwait(false);
                }
                catch (Exception e) {
                    // Swallowed so a teardown failure doesn't replace whatever the body threw.
                    Log.LogWarning(e, "EndCall: teardown failed for chat #{ChatId}", chatId);
                }
            }
            // The session goes on without the call, by the rule every session closes by: once nobody records.
            // The parties' clients stop their media on the call's end - in a peer chat both of them - so that
            // is usually at once. A session the call started closes as the call's card.
            if (await SafeGet(chatId).ConfigureAwait(false) is { IsCall: true })
                await CloseCallSession(chatId).ConfigureAwait(false);
            else
                await StartClosingGrace(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "EndCall failed for chat #{ChatId}", chatId);
        }
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    private async Task MaterializeCallSession(LiveSessionState state)
    {
        // A call's session is materialized when it ends, and unlike a transcript session it has no title to
        // gate on - the card is the point.
        // Only a summary advances EndEntryLid, and a call may never have had one, so the range could end at -
        // or before - the start it was given at the answer, taking the card and the entry it must contain
        // out of the chat. The chat's last id covers the call entry, which the call's end wrote before this.
        var lastLid = (await ChatsBackend
            .GetLidRange(state.ChatId, false, CancellationToken.None)
            .ConfigureAwait(false)).End - 1;
        // StartsAt/EndsAt default to StartedAt (the ring, not the answer) and to LastSummaryAt, which may
        // predate the end by a resummarization delay - so both need the session's real span.
        var conversation = state.ToMaterializedConversation() with {
            EndEntryLid = Math.Max(state.EndEntryLid, lastLid),
            StartsAt = state.SessionStartedAt ?? state.StartedAt,
            EndsAt = Clocks.SystemClock.Now,
        };
        var materialize = new ConversationBackend_Materialize(conversation);
        await Commander.Call(materialize, true, CancellationToken.None).ConfigureAwait(false);
        await WakeCallTailFlow(conversation.Id).ConfigureAwait(false);
        await EnqueueSessionEnded(state).ConfigureAwait(false);
    }

    private async Task CloseCallSession(ChatId chatId)
    {
        // A call's session outlives its call and closes as an ambient one would - once nobody records - but
        // its block stays the call's card, which needs no summary to be kept.
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
        if (await SafeGet(chatId).ConfigureAwait(false) is not { IsCall: true } state)
            return;
        if (await SafeGetCall(chatId).ConfigureAwait(false) is not null)
            return;
        if (await IsSessionLive(chatId).ConfigureAwait(false))
            return;

        await MaterializeCallSession(state).ConfigureAwait(false);
        await RemoveSession(chatId).ConfigureAwait(false);
    }

    // Backstop close: the grace elapsed without an explicit leave (a crashed/stale client). Vanishes
    // the session and sends FINAL itself.
    private async Task SelfClose(ChatId chatId)
    {
        try {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is not { IsClosing: true })
                return;

            await CloseAndMaterialize(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SelfClose failed for chat #{ChatId}", chatId);
        }
    }

    private async Task CloseAndMaterialize(LiveSessionState state, CancellationToken cancellationToken)
    {
        // A call still in a call's session ends first, as only its teardown records it; the session then
        // closes as the call's card.
        if (state.IsCall) {
            await EndCall(state.ChatId, mustRecheckParties: true).ConfigureAwait(false);
            await CloseCallSession(state.ChatId).ConfigureAwait(false);
            return;
        }

        // A session that never latched (solo) leaves its ordinary split-flow conversations behind;
        // only a latched session materializes its summary. There is no completion notification - the
        // in-chat block already carries the summary in place, so an "ended" banner adds nothing.
        // Persist the already-computed summary as a real conversation *before* the live state drops,
        // so the in-chat block doesn't flicker; an empty title (phone-mode, or below the summary
        // threshold) has nothing to keep and just vanishes.
        // The whole close is one locked step that re-checks liveness: callers found the session empty without
        // the lock, and a speaker who came back since has reopened this same session. Dropping it would take
        // their fresh recorder along; keeping it after the write would persist it twice, the second time under
        // whatever context start the summary flow has moved to - i.e. as another card.
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(state.ChatId, cancellationToken).ConfigureAwait(false);
        if (await SafeGet(state.ChatId).ConfigureAwait(false) is not { } freshState)
            return; // Another closer got here first
        if (await IsSessionLive(state.ChatId).ConfigureAwait(false))
            return;

        state = freshState;
        if (state.SessionStartedAt is not null && !state.Title.IsNullOrEmpty())
            await Commander
                .Call(new ConversationBackend_Materialize(state.ToMaterializedConversation()), true, cancellationToken)
                .ConfigureAwait(false);
        await EnqueueSessionEnded(state).ConfigureAwait(false);
        await RemoveSession(state.ChatId).ConfigureAwait(false);
    }

    private async Task EnqueueSessionEnded(LiveSessionState state)
    {
        // Best-effort, like SpeechStartedEvent: a lost event costs the participants one counted
        // session, never the close. Runs before Close, which drops the participant map it reads.
        if (state.SessionStartedAt is not { } startedAt)
            return;

        try {
            var participants = await SafeGetHashMap(state.ChatId).ConfigureAwait(false);
            var members = new Dictionary<AuthorId, Moment>();
            foreach (var authorId in state.AuthorIds)
                members[authorId] = startedAt;
            foreach (var (authorIdValue, info) in participants) {
                if (info is null || !AuthorId.TryParse(authorIdValue, out var authorId))
                    continue;

                var joinedAt = info.JoinedAt == default ? info.RegisteredAt : info.JoinedAt;
                members[authorId] = members.TryGetValue(authorId, out var known) && known < joinedAt ? known : joinedAt;
            }
            var ended = new LiveSessionEndedEvent(
                state.ChatId,
                startedAt,
                Clocks.SystemClock.Now,
                state.Kind,
                members.Select(kv => new LiveSessionEndedMember(kv.Key, kv.Value)).ToApiArray());
            await Services.Queues().Enqueue(ended, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            StreamingMeters.LiveSessionEndedDropped.Add(1);
            Log.LogError(e, "Failed to enqueue LiveSessionEndedEvent for chat '{ChatId}'", state.ChatId);
        }
    }

    private Task WriteCallEntryAfterTail(
        LiveCall call,
        IReadOnlyList<AuthorId> invitees,
        HashSet<AuthorId> partyIds,
        CallOutcome outcome,
        Moment endedAt)
        // A transcript gets its entry on its first non-empty result, so the call's last words get theirs
        // only once the parties' clients have stopped their media and the transcriber has caught up.
        // In memory, like the ring timers: a restart in between leaves the call without its entry.
        => BackgroundTask.Run(async () => {
            var chatId = call.ChatId;
            using (var cts = new CancellationTokenSource(CallStreamsEndTimeout)) {
                var cStreams = await Computed
                    .Capture(() => LiveAudioBackend.List(chatId, cts.Token), cts.Token)
                    .ConfigureAwait(false);
                await cStreams
                    .When(streams => !streams.Any(s => partyIds.Contains(s.AuthorId) && s.BeginsAt < endedAt),
                        cts.Token)
                    .SilentAwait(false);
            }
            await Task.Delay(CallTailDelay).ConfigureAwait(false);
            await WriteCallEntry(call, invitees, outcome, endedAt, CancellationToken.None).ConfigureAwait(false);
        }, Log, $"Writing the entry of call #{call.Id} failed");

    private async Task WriteCallEntry(
        LiveCall call,
        IReadOnlyList<AuthorId> invitees,
        CallOutcome outcome,
        Moment endedAt,
        CancellationToken cancellationToken)
    {
        if (outcome == CallOutcome.None)
            return;

        var chatId = call.ChatId;
        var caller = await AuthorsBackend
            .Get(chatId, call.CallerId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        var command = new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.Call,
                AuthorId = Constants.User.Walle.GetWalleAuthorId(chatId),
                // An answered call's span is its talk time: it is the entry's own Duration, all a call
                // placed into an ongoing session has to show it by - there is no call card around it.
                BeginsAt = call.AnsweredAt ?? endedAt,
                EndsAt = call.IsAnswered ? endedAt : null,
                CallerId = call.CallerId,
                CallerName = caller?.Avatar.Name.NullIfEmpty() ?? MentionMarkup.NotAvailableName,
                Outcome = outcome,
                InviteeIds = invitees.ToApiArray(),
                HasVideo = call.HasVideo,
            }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanUpAfterCall(ChatId chatId, CallId callId)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
        // The ended call's key went first, and the chat has been free since: a call placed meanwhile
        // wrote invites of its own, which aren't this teardown's to drop.
        var nextCall = await SafeGetCall(chatId).ConfigureAwait(false);
        if (nextCall is not null && nextCall.Id != callId)
            Log.LogInformation(
                "EndCall: call #{CallId} is over, keeping the invites of #{NextCallId}", callId, nextCall.Id);
        else
            await _invites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        InvalidateCall(chatId);
        InvalidateState(chatId);
        InvalidateHasRecorder(chatId);
        InvalidateListParticipants(chatId);
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    private async Task RemoveSession(ChatId chatId)
    {
        // Everything derived from the dropped participant map is invalidated with it: HasRecorder
        // otherwise self-heals on a delay, reading a stale streamer as a talker after the session.
        await _redisScope.Remove(chatId.Value).ConfigureAwait(false);
        await _participants.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        // A call still ringing into this session outlives it: the answer will start a session of its own.
        if (await SafeGetCall(chatId).ConfigureAwait(false) is null)
            await _invites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        InvalidateState(chatId);
        InvalidateHasRecorder(chatId);
        InvalidateListParticipants(chatId);
    }

    private Task EnqueueLiveNotification(
        LiveSessionState state,
        ConversationNotificationPhase phase,
        string content,
        CancellationToken cancellationToken)
    {
        // Peer (1:1) chats get no live-session banner - the sole recipient is the other participant,
        // who is already inside the very conversation the session belongs to.
        if (state.ChatId.Kind == ChatKind.Peer)
            return Task.CompletedTask;

        return Services.Queues()
            .Enqueue(
                new NotificationsBackend_NotifyConversation(
                    state.ConversationId, phase, content, state.EndEntryLid, state.AuthorIds),
                cancellationToken);
    }

    private void InvalidateState(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = GetState(chatId, default);
            _ = Get(chatId, default);
        }
    }

    private void InvalidateCall(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = GetCall(chatId, default);
            _ = Get(chatId, default);
        }
    }

    // An invite write alone; InvalidateCall reaches the invites through GetCall.
    private void InvalidateInvites(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListInvites(chatId, default);
    }

    private void InvalidateGet(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = Get(chatId, default);
    }

    private void InvalidateHasRecorder(ChatId chatId)
    {
        // The consolidating methods, not the public ones: IHasInvalidationTarget redirects to the
        // consolidation source, while invalidating a plain derived computed can't reach it.
        using (Invalidation.Begin())
            _ = GetConsolidatedHasRecorder(chatId, default);
    }

    private void InvalidateListParticipants(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetConsolidatedParticipants(chatId, default);
    }

    // Nested types

    [DataContract, MessagePackObject]
    public sealed partial record ParticipationInfo(
        [property: DataMember(Order = 0), Key(0)] ParticipationKind Kind,
        [property: DataMember(Order = 1), Key(1)] Moment RegisteredAt,
        [property: DataMember(Order = 2), Key(2)] bool MicMuted = false,
        [property: DataMember(Order = 3), Key(3)] Moment JoinedAt = default,
        [property: DataMember(Order = 4), Key(4)] Moment? HandRaisedAt = null);
}
