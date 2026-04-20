using ActualChat.Redis;
using ActualChat.Video;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend : ShardComputeService, ILiveVideoBackend
{
    private static readonly TimeSpan RedisTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ChatStateTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<ChatId, ExpiringEntry<ChatId, ChatState>> _chatStates = new();

    private RedisMultiHashMap<VideoStreamInfo> Streams { get; }
    private RedisMultiHashMap<VideoStreamMemberInfo> Members { get; }

    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private MomentClock SystemClock => Clocks.SystemClock;

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        var log = services.LogFor(GetType());
        Streams = new RedisMultiHashMap<VideoStreamInfo>(redisDb, "live-video:streams", log) {
            HashTtl = RedisTtl,
        };
        Members = new RedisMultiHashMap<VideoStreamMemberInfo>(redisDb, "live-video:members", log) {
            HashTtl = RedisTtl,
        };
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        // Adds a dependency on this node's shard ownership state for chatId.
        // Invalidates this computed on any ownership transition (gain/loss/handover),
        // so bound RPC clients get pushed invalidations and reroute to the new owner.
        // Throws RpcRerouteException if the call landed on a node not mapped to this shard.
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var streams = await SafeGetAll(Streams, chatId).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        var meshState = MeshWatcher.State.Value;
        var liveStreams = streams.Values
            .WhereAlive(meshState, static info => info.StreamId)
            .ToList();
        return new(liveStreams);
    }

    // [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var allMembers = await SafeGetAll(Members, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        return activeMembers.Count;
    }

    public virtual async Task Register(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);

        // Read existing streams once — needed for both screencast and webcam checks
        var existingStreams = await SafeGetAll(Streams, chatId).ConfigureAwait(false);

        // Enforce single screencaster per chat — but allow the same author to replace
        // their own prior screencast (mints a new StreamId on every reconnect / pipeline
        // restart; the stale one gets evicted by the same-author/same-kind loop below).
        if (streamInfo.StreamKind == StreamKind.Screencast) {
            var hasForeignScreencast = existingStreams.Values
                .Any(s => s.StreamKind == StreamKind.Screencast
                    && s.AuthorId != streamInfo.AuthorId
                    && s.StreamId != streamInfo.StreamId);
            if (hasForeignScreencast)
                throw new InvalidOperationException("Another screencast is already active in this chat.");
        }

        // Enforce webcam stream cap
        if (streamInfo.StreamKind == StreamKind.Webcam) {
            var webcamCount = existingStreams.Values
                .Count(s => s.StreamKind == StreamKind.Webcam && s.StreamId != streamInfo.StreamId);
            if (webcamCount >= Constants.Video.MaxWebcamStreamsPerChat)
                throw new VideoStreamLimitExceededException(chatId);
        }

        // Evict stale streams from the same author + kind (e.g. after client reconnect creates a new stream)
        foreach (var (key, existing) in existingStreams) {
            if (existing.AuthorId == streamInfo.AuthorId
                && existing.StreamKind == streamInfo.StreamKind
                && existing.StreamId != streamInfo.StreamId) {
                Log.LogWarning("Register: evicting stale stream {OldStreamId} for author {AuthorId} (replaced by {NewStreamId})",
                    existing.StreamId, streamInfo.AuthorId, streamInfo.StreamId);
                await Streams.Remove(chatId.Value,key).ConfigureAwait(false);
            }
        }

        Log.LogWarning("RegisterActiveStream({ChatId}): #{StreamId}, AuthorId={AuthorId}, StreamKind={StreamKind}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId, streamInfo.StreamKind);
        await Streams.Set(chatId.Value, streamInfo.StreamId.Value, streamInfo).ConfigureAwait(false);
        InvalidateListActiveStreams(chatId);
        _ = BackgroundTask.Run(() => EvaluateStreamPriority(chatId, CancellationToken.None), CancellationToken.None);
    }

    public virtual async Task Unregister(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);
        Log.LogWarning("UnregisterActiveStream({ChatId}): #{StreamId}", chatId, streamId);
        var removed = await Streams.Remove(chatId.Value,streamId.Value).ConfigureAwait(false);
        if (removed) {
            InvalidateListActiveStreams(chatId);
            _ = BackgroundTask.Run(() => EvaluateStreamPriority(chatId, CancellationToken.None), CancellationToken.None);
        }
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var allMembers = await SafeGetAll(Members, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        var chatState = GetChatState(chatId);
        chatState.RecomputeCodecs(activeMembers);
        return chatState.GetCurrentSupportedDecoderCodecs();
    }

    // [ComputeMethod]
    public virtual async Task<bool> ShouldPause(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var chatState = GetChatState(chatId);
        return chatState.ShouldPause(streamId);
    }

    public virtual async Task RegisterMember(ChatId chatId, string sessionId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken)
    {
        var memberInfo = new VideoStreamMemberInfo(supportedDecoderCodecs, SystemClock.Now);
        await Members.Set(chatId.Value, sessionId, memberInfo).ConfigureAwait(false);
        var allMembers = await SafeGetAll(Members, chatId).ConfigureAwait(false);
        var (activeMembers, staleKeys) = FilterStaleMembers(chatId, allMembers);

        Log.LogDebug("RegisterVideoStreamMember({ChatId}): session={SessionId}, codecs=[{Codecs}], active={Active}, stale={Stale}",
            chatId, sessionId, string.Join(", ", supportedDecoderCodecs), activeMembers.Count, staleKeys?.Count ?? 0);

        var chatState = GetChatState(chatId);
        chatState.RecomputeCodecs(activeMembers);
        InvalidateGetVideoStreamMemberCount(chatId);
        InvalidateGetSupportedDecoderCodecs(chatId);
    }

    public virtual async Task UnregisterMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        var removed = await Members.Remove(chatId.Value,sessionId).ConfigureAwait(false);
        if (removed) {
            var allMembers = await SafeGetAll(Members, chatId).ConfigureAwait(false);
            var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
            var chatState = GetChatState(chatId);
            chatState.RecomputeCodecs(activeMembers);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
        }
    }

    // Private methods

    private async Task EvaluateStreamPriority(ChatId chatId, CancellationToken cancellationToken)
    {
        var videoStreams = await List(chatId, cancellationToken).ConfigureAwait(false);
        var audioStreams = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        var chatState = GetChatState(chatId);
        chatState.EvaluatePriority(videoStreams, audioStreams, Clocks);
    }

    private static readonly TimeSpan MemberStalenessThreshold = TimeSpan.FromSeconds(90);

    private (Dictionary<string, ApiArray<string>> Active, List<string>? StaleKeys) FilterStaleMembers(
        ChatId chatId,
        Dictionary<string, VideoStreamMemberInfo> allMembers)
    {
        var cutoff = SystemClock.Now - MemberStalenessThreshold;
        var active = new Dictionary<string, ApiArray<string>>(allMembers.Count, StringComparer.Ordinal);
        List<string>? staleKeys = null;

        foreach (var (sessionId, info) in allMembers)
            if (info.RegisteredAt >= cutoff)
                active[sessionId] = info.SupportedDecoderCodecs;
            else {
                staleKeys ??= new();
                staleKeys.Add(sessionId);
            }

        // Fire-and-forget cleanup of stale entries from Redis
        if (staleKeys != null)
            _ = CleanupStaleMembers(chatId, staleKeys);

        return (active, staleKeys);
    }

    private async Task CleanupStaleMembers(ChatId chatId, List<string> staleSessionIds)
    {
        try {
            foreach (var sessionId in staleSessionIds)
                await Members.Remove(chatId.Value,sessionId).ConfigureAwait(false);

            Log.LogDebug("CleanupStaleMembers({ChatId}): removed {Count} stale member(s)", chatId, staleSessionIds.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CleanupStaleMembers({ChatId}): failed to remove stale members", chatId);
        }
    }

    private async Task<Dictionary<string, TValue>> SafeGetAll<TValue>(RedisMultiHashMap<TValue> store, ChatId chatId)
    {
        try {
            var hash = await store.GetHashMap(chatId.Value).ConfigureAwait(false);
            hash.RemoveAll(static (_, v) => v is null);
            return (Dictionary<string, TValue>)(object)hash;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read video state from Redis for chat {ChatId}, returning empty", chatId);
            return new Dictionary<string, TValue>(StringComparer.Ordinal);
        }
    }

    private ExpiringEntry<ChatId, ChatState> BumpChatEntry(ChatId chatId)
    {
        var entry = _chatStates.GetOrAdd(chatId, static (id, self) => {
            var state = new ChatState(self, id);
            var e = ExpiringEntry.New(self._chatStates, id, state);
            e.SetDisposer(self.OnChatStateExpired);
            e.BumpExpiresAt(ChatStateTtl);
            e.BeginExpire();
            return e;
        }, this);
        entry.BumpExpiresAt(ChatStateTtl);
        return entry;
    }

    private ChatState GetChatState(ChatId chatId)
        => BumpChatEntry(chatId).Value;

    private async ValueTask OnChatStateExpired(ExpiringEntry<ChatId, ChatState> entry)
    {
        Log.LogDebug("OnChatStateExpired({ChatId}): cleaning up Redis", entry.Key);
        await Streams.RemoveHashMap(entry.Key.Value).ConfigureAwait(false);
        await Members.RemoveHashMap(entry.Key.Value).ConfigureAwait(false);
        InvalidateListActiveStreams(entry.Key);
        InvalidateGetVideoStreamMemberCount(entry.Key);
        InvalidateGetSupportedDecoderCodecs(entry.Key);
    }

    internal void InvalidateShouldPause(ChatId chatId, StreamId streamId)
    {
        using (Invalidation.Begin())
            _ = ShouldPause(chatId, streamId, default);
    }

    private void InvalidateListActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = List(chatId, default);
    }

    private void InvalidateGetVideoStreamMemberCount(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetVideoStreamMemberCount(chatId, default);
    }

    private void InvalidateGetSupportedDecoderCodecs(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetSupportedCodecs(chatId, default);
    }

}
