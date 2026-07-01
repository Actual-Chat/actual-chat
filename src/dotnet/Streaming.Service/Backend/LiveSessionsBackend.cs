using ActualChat.Chat;
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
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(40);

    private readonly RedisScope<LiveSessionState> _redisScope;
    private readonly RedisMultiHashMap<ParticipationInfo> _participants;
    private readonly RedisMultiHashMap<CallInvite> _invites;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);

    private IChatsBackend ChatsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }
    private ICommander Commander => field ??= Services.Commander();

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
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
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

        // Liveness is participant-driven: once nobody is even listening, begin the close grace.
        if (!state.IsClosing && !await HasParticipant(chatId).ConfigureAwait(false))
            _ = StartClosingGrace(chatId);

        // A call's unanswered rings time out independently of any observer.
        if (state.Kind == LiveSessionKind.Call && await HasStaleRinging(chatId).ConfigureAwait(false))
            _ = ExpireRings(chatId);

        Computed.GetCurrent().Invalidate(SelfHealDelay);
        return state;
    }

    // [ComputeMethod]
    public virtual async Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await GetState(chatId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;
        if (state.SessionStartedAt is null)
            return null;

        var audio = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var video = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var host = state.Host ?? state.AuthorIds[0];
        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var now = Clocks.SystemClock.Now;

        var byAuthor = new Dictionary<AuthorId, LiveSessionMember>();
        LiveSessionMember For(AuthorId a)
            => byAuthor.TryGetValue(a, out var m) ? m : new LiveSessionMember { AuthorId = a, JoinedAt = now };

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
            var authorId = AuthorId.Parse(authorIdValue);
            var m = For(authorId);
            var isRecorder = IsFreshRecorder(info, cutoff);
            var isListener = info.Kind == ParticipationKind.AudioListen && info.RegisteredAt >= cutoff;
            byAuthor[authorId] = m with {
                IsMicOpen = m.IsMicOpen || isRecorder,
                IsListening = m.IsListening || isListener,
                MicMuted = info.MicMuted,
                JoinedAt = info.RegisteredAt,
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
            StartedAt = state.SessionStartedAt ?? state.StartedAt,
            Rules = state.Rules ?? SessionRules.Default,
            Members = members,
            Conversation = state.ToConversation(),
            TranscriptionOn = state.TranscriptionOn,
            Version = state.Version,
            Kind = state.Kind,
            Invites = invites,
        };
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListParticipants(
        ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var authorIds = participants
            .Where(kv => IsFreshParticipant(kv.Value, cutoff))
            .Select(kv => AuthorId.Parse(kv.Key))
            .ToApiArray();
        if (authorIds.Count > 0)
            // Re-check so a stale (left) participant drops without an explicit off signal.
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return authorIds;
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken)
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

    public virtual async Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
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
            if (ReferenceEquals(authorIds, state.AuthorIds) && !state.IsClosing)
                return;

            state = state with {
                AuthorIds = authorIds,
                IsClosing = false,
                ClosingAt = null,
                Version = VersionGenerator.NextVersion(state.Version),
            };
        }

        if (state.SessionStartedAt is null && state.AuthorIds.Count >= 2) {
            state = state with {
                SessionStartedAt = now,
                Version = VersionGenerator.NextVersion(state.Version),
            };
            // START fires at the 2+ latch for both modes; transcription's later Titled updates the same banner.
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
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            if (isActive) {
                // Preserve mute flags across heartbeats / kind changes.
                var existing = await SafeGetParticipant(chatId, authorId).ConfigureAwait(false);
                var info = new ParticipationInfo(kind, Clocks.SystemClock.Now,
                    existing?.MicMuted ?? false);
                await _participants.Set(chatId.Value, authorId.Value, info).ConfigureAwait(false);
            }
            else
                await _participants.Remove(chatId.Value, authorId.Value).ConfigureAwait(false);
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
            InvalidateGet(chatId);
            emptiedByLeave = !isActive && !await HasParticipant(chatId).ConfigureAwait(false);
            // A join/heartbeat or a leave with others still present just re-evaluates liveness; the
            // grace there is the safety net for crashed/stale clients. An explicit leave that empties
            // the call closes it outright below - no waiting on the grace or on a UI observer.
            if (!emptiedByLeave)
                await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        if (emptiedByLeave)
            await CloseNow(chatId).ConfigureAwait(false);
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

        // The first non-empty title promotes the live banner to "Voice chat: {title}" (TITLED fires once).
        var isFirstTitle = state.Title.IsNullOrEmpty() && !summary.Title.IsNullOrEmpty();
        state = state with {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            EndEntryLid = summary.EndEntryLid,
            MessageCount = summary.MessageCount,
            AuthorIds = summary.AuthorIds.Count > 0 ? summary.AuthorIds : state.AuthorIds,
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

    // Voice-call ring lifecycle

    public virtual async Task StartCall(
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        ConversationId conversationId;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var now = Clocks.SystemClock.Now;
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is null) {
                // A call latches immediately: it is "live" the moment it rings, with the caller alone.
                var startEntryLid =
                    (await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false)).End;
                state = new LiveSessionState {
                    ChatId = chatId,
                    StartEntryLid = startEntryLid,
                    EndEntryLid = startEntryLid,
                    StartedAt = now,
                    SessionStartedAt = now,
                    AuthorIds = [callerAuthorId],
                    Host = callerAuthorId,
                    Kind = LiveSessionKind.Call,
                    Version = VersionGenerator.NextVersion(),
                };
                await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
            }
            conversationId = state.ConversationId;
            await EnsureParticipant(chatId, callerAuthorId).ConfigureAwait(false);
            foreach (var invitee in invitees) {
                if (invitee == callerAuthorId)
                    continue;

                await _invites.Set(chatId.Value, invitee.Value,
                        new CallInvite { InviteeId = invitee, Status = CallInviteStatus.Ringing, RingingAt = now })
                    .ConfigureAwait(false);
            }
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

            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite with { Status = CallInviteStatus.Accepted, RespondedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            // Answering joins the call - register now so it's two-party and stays alive before the client streams.
            await EnsureParticipant(chatId, inviteeAuthorId).ConfigureAwait(false);
            conversationId = (await SafeGet(chatId).ConfigureAwait(false))?.ConversationId;
            InvalidateState(chatId);
        }
        if (conversationId is { } cid)
            await DismissRing(cid, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task DeclineCall(ChatId chatId, AuthorId inviteeAuthorId, CancellationToken cancellationToken)
    {
        ConversationId? conversationId = null;
        using (Computed.BeginIsolation())
        using (await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false)) {
            var invite = await SafeGetInvite(chatId, inviteeAuthorId).ConfigureAwait(false);
            if (invite is not { Status: CallInviteStatus.Ringing })
                return;

            await _invites.Set(chatId.Value, inviteeAuthorId.Value,
                    invite with { Status = CallInviteStatus.Declined, RespondedAt = Clocks.SystemClock.Now })
                .ConfigureAwait(false);
            conversationId = (await SafeGet(chatId).ConfigureAwait(false))?.ConversationId;
            InvalidateState(chatId);
        }
        if (conversationId is { } cid)
            await DismissRing(cid, [inviteeAuthorId], cancellationToken).ConfigureAwait(false);
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

            conversationId = state.ConversationId;
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
            InvalidateState(chatId);
            InvalidateListParticipants(chatId);
            InvalidateHasRecorder(chatId);
        }
        if (conversationId is { } cid && ringing.Count > 0)
            await DismissRing(cid, ringing, cancellationToken).ConfigureAwait(false);
        await CloseNow(chatId).ConfigureAwait(false);
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

    private Task DismissRing(
        ConversationId conversationId, IReadOnlyList<AuthorId> invitees, CancellationToken cancellationToken)
        => Services.Queues()
            .Enqueue(new NotificationsBackend_CancelCall(conversationId, invitees), cancellationToken);

    // An unanswered invitee rang past RingTimeout: mark Missed and stop the ring (the caller still
    // sees "missed" and can hang up). Fired observation-independently from GetState's self-heal.
    private async Task ExpireRings(ChatId chatId)
    {
        try {
            ConversationId? conversationId = null;
            var expired = new List<AuthorId>();
            using (Computed.BeginIsolation())
            using (await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false)) {
                var state = await SafeGet(chatId).ConfigureAwait(false);
                if (state is null)
                    return;

                conversationId = state.ConversationId;
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

    // Caller must hold the change lock + Computed.BeginIsolation().
    private async Task EvaluateLiveness(ChatId chatId)
    {
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return;

        // Any present participant (recording, listening, or watching) keeps the session alive;
        // it closes only once nobody is even listening.
        var isActive = await HasParticipant(chatId).ConfigureAwait(false);
        if (isActive == !state.IsClosing)
            return; // already active+open or inactive+closing

        state = isActive
            ? state with { IsClosing = false, ClosingAt = null, Version = VersionGenerator.NextVersion(state.Version) }
            : state with { IsClosing = true, ClosingAt = Clocks.SystemClock.Now, Version = VersionGenerator.NextVersion(state.Version) };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateState(chatId);
    }

    private async Task StartClosingGrace(ChatId chatId)
    {
        try {
            using var _ = Computed.BeginIsolation();
            using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
            await EvaluateLiveness(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartClosingGrace failed for chat #{ChatId}", chatId);
        }
    }

    // Immediate close: the last participant left explicitly, so wind the call down now rather than
    // marking it closing and waiting out the grace. Re-checks under no lock that nobody rejoined first.
    private async Task CloseNow(ChatId chatId)
    {
        try {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is null)
                return;
            if (await HasParticipant(chatId).ConfigureAwait(false))
                return; // someone (re)joined before we got here
            await CloseWithFinal(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CloseNow failed for chat #{ChatId}", chatId);
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
            await CloseWithFinal(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SelfClose failed for chat #{ChatId}", chatId);
        }
    }

    private async Task CloseWithFinal(LiveSessionState state, CancellationToken cancellationToken)
    {
        if (state.Kind == LiveSessionKind.Call) {
            // A voice call has no transcript to materialize or FINAL to post - just stop any lingering
            // rings on the invitees' devices, then drop the session.
            var invitees = (await SafeGetInvites(state.ChatId).ConfigureAwait(false))
                .Values.Where(i => i is not null).Select(i => i!.InviteeId).ToList();
            if (invitees.Count > 0)
                await DismissRing(state.ConversationId, invitees, cancellationToken).ConfigureAwait(false);
            await Close(state.ChatId, cancellationToken).ConfigureAwait(false);
            return;
        }

        // A session that never latched (solo) leaves its ordinary split-flow conversations behind;
        // only a latched session sends FINAL and is covered by its own materialization.
        if (state.SessionStartedAt is not null) {
            // Persist the already-computed summary as a real conversation *before* the live state drops,
            // so the in-chat block doesn't flicker; an empty title (phone-mode, or below the summary
            // threshold) has nothing to keep and just vanishes.
            if (!state.Title.IsNullOrEmpty())
                await Commander
                    .Call(new ConversationBackend_Materialize(state.ToConversation()), true, cancellationToken)
                    .ConfigureAwait(false);
            var content = state.Title.IsNullOrEmpty() ? "Voice chat ended" : $"Voice chat ended: {state.Title}";
            await EnqueueLiveNotification(state, ConversationNotificationPhase.Final, content, cancellationToken)
                .ConfigureAwait(false);
        }
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
        => Services.Queues()
            .Enqueue(
                new NotificationsBackend_NotifyConversation(state.ConversationId, phase, content, state.EndEntryLid, state.AuthorIds),
                cancellationToken);

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
        using (Invalidation.Begin())
            _ = HasRecorder(chatId, default);
    }

    private void InvalidateListParticipants(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListParticipants(chatId, default);
    }

    // Nested types

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
    public sealed partial record ParticipationInfo(
        [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] ParticipationKind Kind,
        [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] Moment RegisteredAt,
        [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] bool MicMuted = false);
}
