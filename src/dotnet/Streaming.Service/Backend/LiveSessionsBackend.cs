using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Media;
using ActualChat.Notification;
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

    private readonly RedisScope<LiveConversation> _redisScope;
    private readonly RedisMultiHashMap<ParticipationInfo> _participants;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);

    private IChatsBackend ChatsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }

    public LiveSessionsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        LiveAudioBackend = services.GetRequiredService<ILiveAudioBackend>();
        LiveVideoBackend = services.GetRequiredService<ILiveVideoBackend>();
        VersionGenerator = services.GetRequiredService<VersionGenerator<long>>();
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _redisScope = new RedisScope<LiveConversation>(redisDb, "live-conv:state", Log) {
            DefaultTtl = KeyTtl,
        };
        _participants = new RedisMultiHashMap<ParticipationInfo>(redisDb, "live-conv:participants", Log) {
            HashTtl = KeyTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<LiveConversation?> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return null;

        if (state is { IsClosing: true, ClosingAt: { } closingAt }
            && Clocks.SystemClock.Now - closingAt > CloseTimeout) {
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
            byAuthor[author.Id] = m with {
                IsListening = m.IsListening || (info.Kind == ParticipationKind.AudioListen && info.RegisteredAt >= cutoff),
                MicMuted = info.MicMuted,
                ForcedMuted = info.ForcedMuted,
                JoinedAt = info.RegisteredAt,
            };
        }

        var members = byAuthor.Values
            .Select(m => m with {
                Group = m.AuthorId == host ? MemberGroup.Host
                    : m.IsMicOpen || m.HasCamera || m.HasScreenShare || m.IsListening ? MemberGroup.Other
                    : MemberGroup.Exited,
            })
            .OrderBy(m => (int)m.Group)
            .ToList();

        return new LiveSession {
            ChatId = chatId,
            Host = host,
            StartedAt = state.StartedAt,
            Rules = state.Rules ?? SessionRules.Default,
            Members = members,
            Conversation = state,
            Version = state.Version,
        };
    }

    // [ComputeMethod]
    public virtual async Task<bool> IsParticipant(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var info = await SafeGetParticipant(chatId, userId).ConfigureAwait(false);
        if (info is null)
            return false;

        var cutoff = Clocks.SystemClock.Now - ParticipantStaleness;
        return info.RegisteredAt >= cutoff;
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
            state = new LiveConversation {
                ChatId = chatId,
                StartEntryLid = startEntryLid,
                EndEntryLid = startEntryLid,
                StartedAt = now,
                AuthorIds = [authorId],
                Host = authorId,
                TranscriptionOn = transcriptionOn,
                Version = VersionGenerator.NextVersion(),
            };
            if (!transcriptionOn)
                // Phone-mode START fires immediately; transcription START waits for the first summary (in the flow).
                await EnqueueLiveNotification(chatId, "Voice chat started", isFinal: false, startEntryLid, cancellationToken).ConfigureAwait(false);
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

        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateGet(chatId);
    }

    public virtual async Task OnStreamsChanged(ChatId chatId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var state = await SafeGet(chatId).ConfigureAwait(false);
        if (state is null)
            return;

        var audio = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var video = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var hasStreams = audio.Count > 0 || video.Count > 0;

        if (hasStreams) {
            if (!state.IsClosing)
                return;

            state = state with { IsClosing = false, ClosingAt = null, Version = VersionGenerator.NextVersion(state.Version) };
            await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
            InvalidateGet(chatId);
            return;
        }

        if (!state.TranscriptionOn) {
            // Phone-mode call: nothing to materialize, the block just disappears.
            await _redisScope.Remove(chatId.Value).ConfigureAwait(false);
            await _participants.RemoveHashMap(chatId.Value).ConfigureAwait(false);
            InvalidateGet(chatId);
            await EnqueueLiveNotification(chatId, "Voice chat ended", isFinal: true, state.StartEntryLid, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (state.IsClosing)
            return;

        // Transcription-on close is finalized by LiveConversationSummaryFlow (materialize or vanish).
        state = state with {
            IsClosing = true,
            ClosingAt = Clocks.SystemClock.Now,
            Version = VersionGenerator.NextVersion(state.Version),
        };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
        InvalidateGet(chatId);
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
        if (isActive) {
            // Preserve mute flags across heartbeats / kind changes.
            var existing = await SafeGetParticipant(chatId, userId).ConfigureAwait(false);
            var info = new ParticipationInfo(kind, Clocks.SystemClock.Now,
                existing?.MicMuted ?? false, existing?.ForcedMuted ?? false);
            await _participants.Set(chatId.Value, userId.Value, info).ConfigureAwait(false);
        }
        else
            await _participants.Remove(chatId.Value, userId.Value).ConfigureAwait(false);
        InvalidateIsParticipant(chatId, userId);
        InvalidateGetLiveSession(chatId);
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

    public virtual async Task UpdateSummary(
        ChatId chatId,
        LiveConversationSummary summary,
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

    private async Task<LiveConversation?> SafeGet(ChatId chatId)
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

    private async Task SelfClose(ChatId chatId)
    {
        try {
            var state = await SafeGet(chatId).ConfigureAwait(false);
            if (state is not { IsClosing: true })
                return;

            var content = state.Title.IsNullOrEmpty() ? "Voice chat ended" : $"Voice chat ended: {state.Title}";
            await EnqueueLiveNotification(chatId, content, isFinal: true, state.StartEntryLid, CancellationToken.None).ConfigureAwait(false);
            await Close(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "SelfClose failed for chat #{ChatId}", chatId);
        }
    }

    private Task EnqueueLiveNotification(ChatId chatId, string content, bool isFinal, long startEntryLid, CancellationToken cancellationToken)
        => Services.Queues()
            .Enqueue(new NotificationsBackend_NotifyLiveConversation(chatId, content, isFinal, startEntryLid), cancellationToken);

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

    private void InvalidateIsParticipant(ChatId chatId, UserId userId)
    {
        using (Invalidation.Begin())
            _ = IsParticipant(chatId, userId, default);
    }

    // Nested types

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
    public sealed partial record ParticipationInfo(
        [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] ParticipationKind Kind,
        [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] Moment RegisteredAt,
        [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] bool MicMuted = false,
        [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] bool ForcedMuted = false);
}
