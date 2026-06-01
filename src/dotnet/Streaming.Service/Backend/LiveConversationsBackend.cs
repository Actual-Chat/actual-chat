using ActualChat.Live;
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
public partial class LiveConversationsBackend : ShardComputeService, ILiveConversationsBackend
{
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan SelfHealDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ParticipantStaleness = TimeSpan.FromSeconds(90);

    private readonly RedisScope<LiveConversation> _redisScope;
    private readonly RedisMultiHashMap<ParticipationInfo> _participants;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);

    private IChatsBackend ChatsBackend { get; }
    private ILiveAudioBackend LiveAudioBackend { get; }
    private ILiveVideoBackend LiveVideoBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }

    public LiveConversationsBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
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
        if (state is not null)
            Computed.GetCurrent().Invalidate(SelfHealDelay);
        return state;
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

            state = state with { IsClosing = false, Version = VersionGenerator.NextVersion(state.Version) };
            await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
            InvalidateGet(chatId);
            return;
        }

        if (!state.TranscriptionOn) {
            // Phone-mode call: nothing to materialize, the block just disappears.
            await _redisScope.Remove(chatId.Value).ConfigureAwait(false);
            InvalidateGet(chatId);
            return;
        }

        if (state.IsClosing)
            return;

        // Transcription-on close is finalized by LiveConversationSummaryFlow (materialize or vanish).
        state = state with { IsClosing = true, Version = VersionGenerator.NextVersion(state.Version) };
        await _redisScope.Set(chatId.Value, state).ConfigureAwait(false);
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
            var info = new ParticipationInfo(kind, Clocks.SystemClock.Now);
            await _participants.Set(chatId.Value, userId.Value, info).ConfigureAwait(false);
        }
        else
            await _participants.Remove(chatId.Value, userId.Value).ConfigureAwait(false);
        InvalidateIsParticipant(chatId, userId);
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

    private void InvalidateGet(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = Get(chatId, default);
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
        [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] Moment RegisteredAt);
}
