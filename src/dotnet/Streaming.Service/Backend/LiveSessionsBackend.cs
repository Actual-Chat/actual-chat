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

    private readonly RedisScope<LiveSessionState> _redisScope;
    private readonly RedisMultiHashMap<ParticipationInfo> _participants;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);

    private IChatsBackend ChatsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }

    public LiveSessionsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
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
    }

    // [ComputeMethod]
    public virtual async Task<LiveSessionState?> Get(ChatId chatId, CancellationToken cancellationToken)
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

        Computed.GetCurrent().Invalidate(SelfHealDelay);
        return state;
    }

    // [ComputeMethod]
    public virtual async Task<LiveSession?> GetLiveSession(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await Get(chatId, cancellationToken).ConfigureAwait(false);
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
        foreach (var (userIdValue, info) in participants) {
            if (info is null)
                continue;
            var author = await AuthorsBackend
                .GetByUserId(chatId, UserId.Parse(userIdValue), RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author is null)
                continue;
            var m = For(author.Id);
            var isRecorder = IsFreshRecorder(info, cutoff);
            var isListener = info.Kind == ParticipationKind.AudioListen && info.RegisteredAt >= cutoff;
            byAuthor[author.Id] = m with {
                IsMicOpen = m.IsMicOpen || isRecorder,
                IsListening = m.IsListening || isListener,
                MicMuted = info.MicMuted,
                JoinedAt = info.RegisteredAt,
            };
        }

        // Crash backstop: if the session is alive only on (now-vanished) signals, start the grace.
        // Reuses the already-read streams/participants — no extra Redis round-trips.
        if (!state.IsClosing && audio.Count == 0 && video.Count == 0
            && !participants.Values.Any(p => IsFreshParticipant(p, cutoff)))
            _ = StartClosingGrace(chatId);

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

        return new LiveSession {
            ChatId = chatId,
            Host = host,
            StartedAt = state.SessionStartedAt ?? state.StartedAt,
            Rules = state.Rules ?? SessionRules.Default,
            Members = members,
            Conversation = state.ToConversation(),
            TranscriptionOn = state.TranscriptionOn,
            Version = state.Version,
        };
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserId>> ListParticipants(
        ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        var userIds = participants
            .Where(kv => IsFreshParticipant(kv.Value, cutoff))
            .Select(kv => UserId.Parse(kv.Key))
            .ToApiArray();
        if (userIds.Count > 0)
            // Re-check so a stale (left) participant drops without an explicit off signal.
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return userIds;
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
        await EnsureParticipant(chatId, authorId, cancellationToken).ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task OnStreamsChanged(ChatId chatId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);
        await EvaluateLiveness(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task Close(ChatId chatId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        await _redisScope.Remove(chatId.Value).ConfigureAwait(false);
        await _participants.RemoveHashMap(chatId.Value).ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task SetParticipation(
        ChatId chatId,
        UserId userId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        if (isActive) {
            // Preserve mute flags across heartbeats / kind changes.
            var existing = await SafeGetParticipant(chatId, userId).ConfigureAwait(false);
            var info = new ParticipationInfo(kind, Clocks.SystemClock.Now,
                existing?.MicMuted ?? false);
            await _participants.Set(chatId.Value, userId.Value, info).ConfigureAwait(false);
        }
        else
            await _participants.Remove(chatId.Value, userId.Value).ConfigureAwait(false);
        InvalidateListParticipants(chatId);
        InvalidateHasRecorder(chatId);
        InvalidateGetLiveSession(chatId);
        // Recording turned on/off flips liveness (a vanished last recorder starts the grace).
        await EvaluateLiveness(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task SetMicMuted(
        ChatId chatId,
        UserId userId,
        bool micMuted,
        CancellationToken cancellationToken)
    {
        var existing = await SafeGetParticipant(chatId, userId).ConfigureAwait(false);
        if (existing is null)
            return;
        await _participants.Set(chatId.Value, userId.Value, existing with { MicMuted = micMuted }).ConfigureAwait(false);
        InvalidateGetLiveSession(chatId);
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
        InvalidateGet(chatId);
    }

    public virtual async Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend
            .Get(chatId, targetAuthorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        if (author is null)
            return;
        await EnsureParticipant(chatId, targetAuthorId, cancellationToken).ConfigureAwait(false);
        var existing = await SafeGetParticipant(chatId, author.UserId).ConfigureAwait(false);
        if (existing is null)
            return;
        await _participants.Set(chatId.Value, author.UserId.Value, existing with { MicMuted = muted }).ConfigureAwait(false);
        InvalidateGetLiveSession(chatId);
    }

    public virtual async Task MuteAll(ChatId chatId, AuthorId exceptAuthorId, bool muted, CancellationToken cancellationToken)
    {
        // Soft "mute all except the actor": sets MicMuted on every other participant.
        // This is peer-revocable — a muted peer can re-record to unmute themselves.
        var exceptUserId = (await AuthorsBackend
            .Get(chatId, exceptAuthorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false))?.UserId;
        var participants = await SafeGetHashMap(chatId).ConfigureAwait(false);
        foreach (var (userIdValue, info) in participants) {
            if (info is null || info.MicMuted == muted)
                continue;
            if (exceptUserId is { } id && userIdValue == id.Value)
                continue;

            await _participants.Set(chatId.Value, userIdValue, info with { MicMuted = muted }).ConfigureAwait(false);
        }
        InvalidateGetLiveSession(chatId);
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
        InvalidateGet(chatId);
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

    private async Task<ParticipationInfo?> SafeGetParticipant(ChatId chatId, UserId userId)
    {
        try {
            return await _participants.Get(chatId.Value, userId.Value).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read participants from Redis for chat #{ChatId}", chatId);
            return null;
        }
    }

    private async Task EnsureParticipant(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        if (author is null || author.UserId.Value.IsNullOrEmpty())
            return;
        var existing = await SafeGetParticipant(chatId, author.UserId).ConfigureAwait(false);
        // Preserve the client's real kind — a trailing utterance must not flip a now-listening user back to Record.
        var kind = existing?.Kind ?? ParticipationKind.Record;
        var info = new ParticipationInfo(kind, Clocks.SystemClock.Now,
            existing?.MicMuted ?? false);
        await _participants.Set(chatId.Value, author.UserId.Value, info).ConfigureAwait(false);
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

    private async Task<bool> HasLiveSignal(ChatId chatId, CancellationToken cancellationToken)
    {
        var audio = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        if (audio.Count > 0)
            return true;
        var video = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        if (video.Count > 0)
            return true;
        // Any present participant (recording, listening, or watching) keeps the session alive;
        // it closes only once nobody is even listening.
        return await HasParticipant(chatId).ConfigureAwait(false);
    }

    // Caller must hold the change lock + Computed.BeginIsolation().
    private async Task EvaluateLiveness(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return;

        var isActive = await HasLiveSignal(chatId, cancellationToken).ConfigureAwait(false);
        if (isActive == !state.IsClosing)
            return; // already active+open or inactive+closing

        state = isActive
            ? state with { IsClosing = false, ClosingAt = null, Version = VersionGenerator.NextVersion(state.Version) }
            : state with { IsClosing = true, ClosingAt = Clocks.SystemClock.Now, Version = VersionGenerator.NextVersion(state.Version) };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    private async Task StartClosingGrace(ChatId chatId)
    {
        try {
            using var _ = Computed.BeginIsolation();
            using var lockHolder = await _changeLocks.Lock(chatId, CancellationToken.None).ConfigureAwait(false);
            await EvaluateLiveness(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "StartClosingGrace failed for chat #{ChatId}", chatId);
        }
    }

    private async Task SelfClose(ChatId chatId)
    {
        try {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is not { IsClosing: true })
                return;

            // A session that never latched (solo) leaves its ordinary split-flow conversations behind;
            // only a latched session sends FINAL and is covered by its own materialization.
            if (state.SessionStartedAt is not null) {
                var content = state.Title.IsNullOrEmpty() ? "Voice chat ended" : $"Voice chat ended: {state.Title}";
                await EnqueueLiveNotification(state, ConversationNotificationPhase.Final, content, CancellationToken.None).ConfigureAwait(false);
            }
            await Close(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SelfClose failed for chat #{ChatId}", chatId);
        }
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

    private void InvalidateGet(ChatId chatId)
    {
        using (Invalidation.Begin()) {
            _ = Get(chatId, default);
            _ = GetLiveSession(chatId, default);
        }
    }

    private void InvalidateGetLiveSession(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetLiveSession(chatId, default);
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
