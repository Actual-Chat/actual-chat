using ActualChat.Chat;
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
public partial class LiveSessionsBackend : ShardComputeService, ILiveSessionsBackend
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
    // How long a call rings an unanswered invitee before it's marked Missed and the ring stops.
    // TODO: revert to 40s - lowered to 20s only for call-status testing.
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(20);
    // Redis field TTL on a ringing invite: the no-observer backstop that expires a stale ring even
    // if the caller disconnects and nobody polls GetState. Longer than RingTimeout so the observed
    // path marks it Missed first; a status change rewrites the field without this short TTL.
    private static readonly TimeSpan RingTtl = TimeSpan.FromSeconds(60);
    // How long a dialing call state lingers with no observer before its Redis key lapses - it covers
    // the ring plus a backstop; a terminal transition overwrites it sooner.
    private static readonly TimeSpan DialingStateTtl = TimeSpan.FromSeconds(60);
    // How long the caller keeps being shown a resolved call status (accepted / declined / no answer),
    // once the session itself is gone.
    private static readonly TimeSpan ResolvedStateTtl = TimeSpan.FromSeconds(30);

    private readonly RedisScope<LiveSessionState> _redisScope;
    private readonly RedisScope<CallState> _callStates;
    private readonly RedisMultiHashMap<ParticipationInfo> _participants;
    private readonly RedisMultiHashMap<CallInvite> _invites;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);

    private IChatsBackend ChatsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }
    private ICommander Commander => field ??= Services.Commander();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    // Wake the summary flow so it runs its final pass at once instead of on its next throttled resume -
    // that's what makes a closing block finalize (and dissolve) promptly. By FlowId string name so the
    // streaming backend needn't reference the Chat.Service flow type. WithDelay(0, 0) is immediate AND
    // un-quantized - without the zero quantum the resume inherits the flow's 15s DelayQuanta and rounds
    // up to the next 15s boundary, coalescing with the throttle (defeating the whole point).
    private Task WakeSummaryFlow(ChatId chatId)
        => FlowHub.NewResumeEvent(new FlowId(LiveFlows.SummaryFlowName, chatId.Value))
            .WithDelay(TimeSpan.Zero, TimeSpan.Zero)
            .Schedule(CancellationToken.None);

    public LiveSessionsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        LiveAudioBackend = services.GetRequiredService<ILiveAudioBackend>();
        LiveVideoBackend = services.GetRequiredService<ILiveVideoBackend>();
        VersionGenerator = services.GetRequiredService<VersionGenerator<long>>();
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _redisScope = new RedisScope<LiveSessionState>(redisDb, "live-session:state", Log) {
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
        var computed = Computed.GetCurrent();
        var startedAt = CpuTimestamp.Now;
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);
        var ownershipWaitMs = (long)startedAt.Elapsed.TotalMilliseconds;
        if (ownershipWaitMs > 250)
            Log.LogWarning(
                "GetState: waited {WaitMs}ms for shard ownership of chat #{ChatId}",
                ownershipWaitMs, chatId);

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

        // Prompt ring timeout while this session is observed (the self-heal below re-runs GetState);
        // the RingTtl field expiry in Redis is the backstop when no one observes. A dialing call whose
        // ring has already lapsed (or vanished via RingTtl) is finalized too, so it can't linger unanswered.
        if (state.IsCall && await HasStaleRinging(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);
        else if (state.IsDialing && !await HasFreshRing(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);

        // Required, not merely defensive: IsSessionLive reads raw Redis against a time-based staleness
        // cutoff, so nothing invalidates this on its own. The churn it creates is filtered before it can
        // reach the conversation metadata cache - see GetConsolidatedVisibleStartLid.
        computed.Invalidate(SelfHealDelay);
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
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;
        if (state.SessionStartedAt is null && !state.IsCall)
            return null;

        var audio = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var video = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var host = state.Host ?? state.AuthorIds[0];
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        // Stands in as JoinedAt for stream-only members - they have no participation record to date the
        // join with. Never Clocks.Now: that makes every recompute a different LiveSession, so no
        // consumer can consolidate a no-op invalidation away.
        var startedAt = state.SessionStartedAt ?? state.StartedAt;

        var byAuthor = new Dictionary<AuthorId, LiveSessionMember>();
        LiveSessionMember For(AuthorId a)
            => byAuthor.TryGetValue(a, out var m) ? m : new() { AuthorId = a, JoinedAt = startedAt };

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
                // RegisteredAt is a liveness stamp rewritten by every heartbeat - only the fallback
                // for participation records written before JoinedAt existed.
                JoinedAt = info.JoinedAt == default ? info.RegisteredAt : info.JoinedAt,
            };
        }
        // Owners are grouped with the host (they can manage the call too).
        var ownerRole = (await RolesBackend.ListSystem(chatId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.SystemRole == SystemRole.Owner);
        var ownerIds = ownerRole is null
            ? new HashSet<AuthorId>()
            : (await RolesBackend.ListAuthorIds(chatId, ownerRole.Id, cancellationToken).ConfigureAwait(false))
                .ToHashSet();

        var members = byAuthor.Values
            .Select(m => m with {
                Group = m.AuthorId == host || ownerIds.Contains(m.AuthorId) ? MemberGroup.Host
                    : m.IsMicOpen || m.HasCamera || m.HasScreenShare || m.IsListening ? MemberGroup.Other
                    : MemberGroup.Exited,
            })
            .OrderBy(m => (int)m.Group)
            .ToList();

        var invites = (await SafeGetInvites(chatId).ConfigureAwait(false))
            .Values
            .Where(i => i is not null)
            .Select(i => i!)
            .OrderBy(i => i.RingingAt)
            .ToList();

        return new LiveSession {
            ChatId = chatId,
            Host = host,
            StartedAt = startedAt,
            Rules = state.Rules ?? SessionRules.Default,
            Members = members,
            Conversation = state.IsDialing ? null : state.ToConversation(),
            TranscriptionOn = state.TranscriptionOn,
            Version = state.Version,
            Kind = state.Kind,
            Invites = invites,
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
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

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

    public virtual async Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();

        // Before the dedup/early-return below: the walkie-talkie wake trigger fires per utterance.
        await Services.Queues()
            .Enqueue(new SpeechStartedEvent(chatId, authorId, Clocks.SystemClock.Now), cancellationToken)
            .ConfigureAwait(false);

        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null) {
            var startEntryLid = transcriptionOn && entryLid is { } lid
                ? lid
                : (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            state = new LiveSessionState {
                ChatId = chatId,
                StartEntryLid = startEntryLid,
                EndEntryLid = startEntryLid,
                StartedAt = now,
                AuthorIds = [authorId],
                Host = authorId,
                TranscriptionOn = transcriptionOn,
                Version = VersionGenerator.NextVersion(),
            };
        }
        else {
            var authorIds = state.AuthorIds.Contains(authorId)
                ? state.AuthorIds
                : [..state.AuthorIds, authorId];
            if (ReferenceEquals(authorIds, state.AuthorIds) && !state.IsClosing) {
                // Nothing to write, but a re-registering stream is proof of life: keep the key alive.
                await _redisScope.Refresh(chatId.Value).ConfigureAwait(false);
                return;
            }

            state = state with {
                AuthorIds = authorIds,
                IsClosing = false,
                ClosingAt = null,
                Version = VersionGenerator.NextVersion(state.Version),
            };
        }

        if (state.SessionStartedAt is null && state.AuthorIds.Count >= 2) {
            var visibleStartLid = (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            state = state with {
                // A dialing call latching here (streamed before a formal accept) becomes a connected call,
                // never a Dialing session with SessionStartedAt set.
                Kind = state.Kind == LiveSessionKind.Dialing ? LiveSessionKind.Call : state.Kind,
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
        var startedClosing = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            if (isActive) {
                // Preserve mute flags and the original join time across heartbeats / kind changes:
                // RegisteredAt is refreshed by every heartbeat, so it can't double as JoinedAt.
                var now = Clocks.SystemClock.Now;
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                var joinedAt = existing is { JoinedAt: var j } && j != default ? j : now;
                var info = new ParticipationInfo(kind, now, existing?.MicMuted ?? false, joinedAt);
                await _participants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
                // The participants hash refreshes its own TTL on write, but the session state key only
                // does so on Set - which a steady-state session never reaches. Without this the state
                // silently expires mid-call and the next stream rebuilds it as a brand-new session.
                await _redisScope.Refresh(chatId.Value).ConfigureAwait(false);
            }
            else
                await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
            InvalidateGet(chatId);
            emptiedByLeave = !isActive && !await IsSessionLive(chatId).ConfigureAwait(false);
            // A join/heartbeat, or a leave with someone still streaming, just re-evaluates liveness; the
            // grace there is the safety net for crashed/stale clients. A leave that stops the last stream
            // closes it outright below - no waiting on the grace or on a UI observer. EvaluateLiveness only
            // marks a still-populated session closing (recoverable if a recorder returns), so a transient
            // not-live blip never tears down a live recording - unlike an unconditional CloseNow here would.
            if (!emptiedByLeave)
                startedClosing = await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        if (emptiedByLeave)
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

    public virtual async Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken)
    {
        await EnsureParticipant(chatId, targetAuthorId).ConfigureAwait(false);
        var existing = await SafeGetParticipant(chatId, targetAuthorId).ConfigureAwait(false);
        if (existing is null)
            return;
        await _participants.Set(chatId.Value, targetAuthorId.Value, existing with { MicMuted = muted }).ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task MuteAll(ChatId chatId, AuthorId exceptAuthorId, bool muted, CancellationToken cancellationToken)
    {
        // Soft "mute all except the actor": sets MicMuted on every other participant.
        // This is peer-revocable — a muted peer can re-record to unmute themselves.
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        foreach (var (authorIdValue, info) in participants) {
            if (info is null || info.MicMuted == muted)
                continue;
            if (authorIdValue == exceptAuthorId.Value)
                continue;

            await _participants.Set(chatId.Value, authorIdValue, info with { MicMuted = muted }).ConfigureAwait(false);
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

    public virtual async Task StartCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        // Ring each distinct invitee except the caller - once, and never the caller themselves.
        invitees = invitees.Where(id => id != callerAuthorId).Distinct().ToApiArray();
        ConversationId conversationId;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var now = Clocks.SystemClock.Now;
            var state = await SafeGet(chatId).ConfigureAwait(false);
            var lidRangeEnd = (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
            var startEntryLid = state?.StartEntryLid ?? lidRangeEnd;
            // A fresh call is Dialing: the session exists (for the Call tab and the ring) but no live
            // conversation is surfaced until someone answers, so SessionStartedAt stays null. Promoting an
            // already-latched (ambient) session keeps it connected so its block and ring/close paths persist.
            state = (state ?? new LiveSessionState { ChatId = chatId, StartEntryLid = startEntryLid }) with {
                EndEntryLid = state?.EndEntryLid ?? startEntryLid,
                StartedAt = state?.StartedAt ?? now,
                SessionStartedAt = state?.SessionStartedAt,
                VisibleStartLid = state?.VisibleStartLid ?? 0,
                AuthorIds = state?.AuthorIds is { Count: > 0 } ids ? ids : [callerAuthorId],
                Host = callerAuthorId,
                Kind = state?.SessionStartedAt is not null ? LiveSessionKind.Call : LiveSessionKind.Dialing,
                Version = VersionGenerator.NextVersion(state?.Version ?? 0),
            };
            await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
            // Dialing status only for a fresh call; promoting an already-connected session isn't "calling".
            await SetCallState(chatId, state.IsDialing ? NewCallState(state, CallStatus.Dialing) : null)
                .ConfigureAwait(false);
            conversationId = state.RingConversationId;
            await EnsureParticipant(chatId, callerAuthorId).ConfigureAwait(false);
            foreach (var invitee in invitees)
                await _invites.Set(chatId.Value, invitee.Value,
                        new CallInvite { InviteeId = invitee, Status = CallInviteStatus.Ringing, RingingAt = now },
                        RingTtl)
                    .ConfigureAwait(false);
            InvalidateState(chatId);
        }
        if (invitees.Count > 0)
            await Services.Queues()
                .Enqueue(
                    new NotificationsBackend_NotifyCall(conversationId, callerAuthorId, invitees, hasVideo),
                    cancellationToken)
                .ConfigureAwait(false);
    }

    public virtual async Task AcceptCall(ChatId chatId, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        ConversationId? conversationId = null;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (invite is not { Status: CallInviteStatus.Ringing })
                return;

            var now = Clocks.SystemClock.Now;
            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite with { Status = CallInviteStatus.Accepted, RespondedAt = now })
                .ConfigureAwait(false);
            // Answering joins the call - register now so it's two-party and stays alive before the client streams.
            await EnsureParticipant(chatId, inviteeAuthorId).ConfigureAwait(false);

            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is { SessionStartedAt: null }) {
                // The first answer latches a dialing call to Connected: it's now a live conversation, so
                // surface the block from the chat end at answer time and make it genuinely two-party.
                var visibleStartLid = (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
                var authorIds = state.AuthorIds.Contains(inviteeAuthorId)
                    ? state.AuthorIds
                    : [..state.AuthorIds, inviteeAuthorId];
                state = state with {
                    Kind = LiveSessionKind.Call,
                    SessionStartedAt = now,
                    VisibleStartLid = visibleStartLid,
                    AuthorIds = authorIds,
                    Version = VersionGenerator.NextVersion(state.Version),
                };
                await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
                // The latch is the caller's "accepted" moment - a brief confirmation before this fades.
                await SetCallState(chatId, NewCallState(state, CallStatus.Accepted)).ConfigureAwait(false);
            }
            conversationId = state?.RingConversationId;
            InvalidateState(chatId);
        }
        if (conversationId is { } cid)
            await DismissRing(cid, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task DeclineCall(ChatId chatId, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        ConversationId? conversationId = null;
        var abandoned = false;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (invite is not { Status: CallInviteStatus.Ringing })
                return;

            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite with { Status = CallInviteStatus.Declined, RespondedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            var state = await SafeGet(chatId).ConfigureAwait(false);
            conversationId = state?.RingConversationId;
            abandoned = await IsCallAbandoned(chatId).ConfigureAwait(false);
            // Recorded before the close below drops the session that carries the caller's identity.
            if (abandoned && state is not null)
                await SetCallState(chatId, NewCallState(state, CallStatus.Declined)).ConfigureAwait(false);
            InvalidateState(chatId);
        }
        if (conversationId is { } cid)
            await DismissRing(cid, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
        if (abandoned)
            await CloseCall(chatId).ConfigureAwait(false);
    }

    public virtual async Task CancelCall(ChatId chatId, AuthorId callerAuthorId, CancellationToken cancellationToken)
    {
        // The caller hangs up: stop every still-ringing invitee, drop the caller, then close if empty.
        ConversationId? conversationId = null;
        var ringing = new List<AuthorId>();
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is null)
                return;

            conversationId = state.RingConversationId;
            var now = Clocks.SystemClock.Now;
            foreach (var info in (await SafeGetInvites(chatId).ConfigureAwait(false)).Values) {
                if (info is not { Status: CallInviteStatus.Ringing })
                    continue;

                ringing.Add(info.InviteeId);
                await _invites.Set(chatId.Value, info.InviteeId.Value,
                        info with { Status = CallInviteStatus.Missed, RespondedAt = now })
                    .ConfigureAwait(false);
            }
            await _participants.Remove(chatId.Value, callerAuthorId.Value).ConfigureAwait(false);
            // Hanging up myself needs no status, and it must beat a decline that just landed.
            await SetCallState(chatId, null).ConfigureAwait(false);
            InvalidateState(chatId);
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
        }
        if (conversationId is { } cid && ringing.Count > 0)
            await DismissRing(cid, ringing, cancellationToken).ConfigureAwait(false);
        await CloseNow(chatId).ConfigureAwait(false);
    }

    public virtual Task DismissCallStatus(ChatId chatId, CancellationToken cancellationToken)
        => SetCallState(chatId, null);

    public virtual async Task LeaveCall(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        // A participant hangs up. A call needs at least two people, so once fewer than two remain it is
        // over - close it (which also stops any lingering rings) rather than leaving someone on alone.
        bool close;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is null)
                return;

            await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
            InvalidateGet(chatId);
            close = await ParticipantCount(chatId).ConfigureAwait(false) < 2;
            if (!close)
                await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        if (close)
            await CloseCall(chatId).ConfigureAwait(false);
    }

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
        // Captured before the awaits below — see GetState.
        var computed = Computed.GetCurrent();
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var authorIds = participants
            .Where(kv => IsFreshParticipant(kv.Value, cutoff))
            .Select(kv => (Ok: AuthorId.TryParse(kv.Key, out var id), Id: id))
            .Where(x => x.Ok)
            .Select(x => x.Id)
            .ToApiArray();
        if (authorIds.Count > 0)
            // Re-check so a stale (left) participant drops without an explicit off signal.
            computed.Invalidate(SelfHealDelay);
        return authorIds;
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    protected virtual async Task<bool> GetConsolidatedHasRecorder(ChatId chatId, CancellationToken cancellationToken)
    {
        // Captured before the awaits below — see GetState.
        var computed = Computed.GetCurrent();
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var hasRecorder = participants.Values.Any(p => IsFreshRecorder(p, cutoff));
        if (hasRecorder)
            // Re-check so a stale (crashed) recorder drops without an explicit off signal.
            computed.Invalidate(SelfHealDelay);
        return hasRecorder;
    }

    // Private methods

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
        // Preserve the client's real kind — a trailing utterance must not flip a now-listening author back to Record.
        var kind = existing?.Kind ?? ParticipationKind.Record;
        var info = new ParticipationInfo(kind, Clocks.SystemClock.Now, existing?.MicMuted ?? false);
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

    private CallState NewCallState(LiveSessionState state, CallStatus status)
        => new() {
            CallerId = state.Host ?? state.AuthorIds[0],
            Status = status,
            ChangedAt = Clocks.SystemClock.Now,
        };

    private static TimeSpan CallStateTtl(CallStatus status)
        => status == CallStatus.Dialing ? DialingStateTtl : ResolvedStateTtl;

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

    private Task DismissRing(
        ConversationId conversationId, IReadOnlyList<AuthorId> invitees, CancellationToken cancellationToken)
        => Services.Queues()
            .Enqueue(new NotificationsBackend_CancelCall(conversationId, invitees), cancellationToken);

    // An unanswered invitee rang past RingTimeout: mark Missed and stop the ring (the caller still
    // sees "missed" and can hang up). Fired observation-independently from GetState's self-heal.
    // A dialing call is finalized here even when no invite is left to expire — a ring can vanish via
    // its RingTtl before this catches it, and the call must still reach an outcome rather than linger.
    private async Task ExpireRings(ChatId chatId)
    {
        try {
            ConversationId? conversationId = null;
            var expired = new List<AuthorId>();
            var wasDialing = false;
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                var state = await SafeGet(chatId).ConfigureAwait(false);
                if (state is null)
                    return;

                conversationId = state.RingConversationId;
                wasDialing = state.IsDialing;
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
                    InvalidateState(chatId);
            }
            if (expired.Count > 0 && conversationId is { } cid)
                await DismissRing(cid, expired, CancellationToken.None).ConfigureAwait(false);
            if ((expired.Count > 0 || wasDialing) && await IsCallAbandoned(chatId).ConfigureAwait(false)) {
                // Only a still-dialing call gets a "no answer" - a connected one that emptied out just closes.
                if (await SafeGet(chatId).ConfigureAwait(false) is { IsDialing: true } current
                    && await SafeGetCallState(chatId).ConfigureAwait(false) is null or { Status: CallStatus.Dialing })
                    await SetCallState(chatId, NewCallState(current, CallStatus.NoAnswer)).ConfigureAwait(false);
                await CloseCall(chatId).ConfigureAwait(false);
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
    // don't keep it alive. A still-ringing call is kept alive until it connects or IsCallAbandoned
    // times it out, so a call that hasn't started streaming yet isn't torn down mid-ring.
    private async Task<bool> IsSessionLive(ChatId chatId)
    {
        if (await HasFreshRecorder(chatId).ConfigureAwait(false))
            return true;
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        return invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing });
    }

    private async Task<int> ParticipantCount(ChatId chatId)
    {
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        return participants.Values.Count(p => IsFreshParticipant(p, cutoff));
    }

    private async Task<bool> IsCallAbandoned(ChatId chatId)
    {
        // No invite is still ringing and nobody joined (only the caller, if that): the call can't become
        // two-party, so it's abandoned - the whole thing should close.
        var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
        if (invites.Values.Any(i => i is { Status: CallInviteStatus.Ringing }))
            return false;

        return await ParticipantCount(chatId).ConfigureAwait(false) < 2;
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    // Returns true iff this call just transitioned the session into IsClosing.
    private async Task<bool> EvaluateLiveness(ChatId chatId)
    {
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return false;

        // A streamer (recording audio or video) keeps the session alive; it closes once nobody is
        // streaming, even if listeners/watchers remain.
        var isActive = await IsSessionLive(chatId).ConfigureAwait(false);
        if (isActive == !state.IsClosing)
            return false; // already active+open or inactive+closing

        state = isActive
            ? state with { IsClosing = false, ClosingAt = null, Version = VersionGenerator.NextVersion(state.Version) }
            : state with { IsClosing = true, ClosingAt = Clocks.SystemClock.Now, Version = VersionGenerator.NextVersion(state.Version) };
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

    // Unconditional call teardown: unlike CloseNow it doesn't require the session to be empty first -
    // a call left with a single participant is already over, so it winds down with them still present.
    private async Task CloseCall(ChatId chatId)
    {
        try {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is null)
                return;
            await CloseAndMaterialize(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CloseCall failed for chat #{ChatId}", chatId);
        }
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
        if (state.IsCall) {
            // A voice call has no transcript to materialize - just stop any lingering rings on the
            // invitees' devices, then drop the session.
            var invitees = (await SafeGetInvites(state.ChatId).ConfigureAwait(false))
                .Values.Where(i => i is not null).Select(i => i!.InviteeId).ToList();
            if (invitees.Count > 0)
                await DismissRing(state.RingConversationId, invitees, cancellationToken).ConfigureAwait(false);
            await Close(state.ChatId, cancellationToken).ConfigureAwait(false);
            return;
        }

        // A session that never latched (solo) leaves its ordinary split-flow conversations behind;
        // only a latched session materializes its summary. There is no completion notification - the
        // in-chat block already carries the summary in place, so an "ended" banner adds nothing.
        // Persist the already-computed summary as a real conversation *before* the live state drops,
        // so the in-chat block doesn't flicker; an empty title (phone-mode, or below the summary
        // threshold) has nothing to keep and just vanishes.
        if (state.SessionStartedAt is not null && !state.Title.IsNullOrEmpty())
            await Commander
                .Call(new ConversationBackend_Materialize(state.ToMaterializedConversation()), true, cancellationToken)
                .ConfigureAwait(false);
        await Close(state.ChatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task Close(ChatId chatId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        await _redisScope.Remove(chatId.Value).ConfigureAwait(false);
        await _participants.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        await _invites.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        InvalidateState(chatId);
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
                new NotificationsBackend_NotifyConversation(state.ConversationId, phase, content, state.EndEntryLid, state.AuthorIds),
                cancellationToken);
    }

    private void InvalidateState(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = GetState(chatId, default);
            _ = Get(chatId, default);
        }
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
        [property: DataMember(Order = 3), Key(3)] Moment JoinedAt = default);
}
