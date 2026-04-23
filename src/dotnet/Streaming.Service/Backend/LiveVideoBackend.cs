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
    private readonly LockingComputeMethodPrimer<ChatId, ApiArray<VideoStreamInfo>> _listRawPrimer;
    private readonly RedisMultiHashMap<VideoStreamInfo> _streams;
    private readonly RedisMultiHashMap<VideoStreamMemberInfo> _members;

    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private MomentClock SystemClock => Clocks.SystemClock;

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        var log = services.LogFor(GetType());
        _streams = new RedisMultiHashMap<VideoStreamInfo>(redisDb, "live-video:streams", log) {
            HashTtl = RedisTtl,
        };
        _members = new RedisMultiHashMap<VideoStreamMemberInfo>(redisDb, "live-video:members", log) {
            HashTtl = RedisTtl,
        };
        _listRawPrimer = new LockingComputeMethodPrimer<ChatId, ApiArray<VideoStreamInfo>>(ListRaw);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        // Adds a dependency on this node's shard ownership state for chatId.
        // Invalidates this computed on any ownership transition (gain/loss/handover),
        // so bound RPC clients get pushed invalidations and reroute to the new owner.
        // Throws RpcRerouteException if the call landed on a node not mapped to this shard.
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var streams = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        var meshState = MeshWatcher.State.Value;
        return streams
            .WhereAlive(meshState, static info => info.StreamId)
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var allMembers = await SafeGetAll(_members, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        return activeMembers.Count;
    }

    public virtual async Task Register(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);

        using var isolation = Computed.BeginIsolation();
        using var primer = await _listRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var prev = await List(chatId, cancellationToken).ConfigureAwait(false);

        // Heartbeat fast path: caller re-registers the same stream on an interval to
        // keep the chat state alive. If an identical entry already exists (record
        // value equality), skip the Redis write + primer.Prime to avoid unnecessary
        // invalidation cycles that would churn all bound RPC subscribers.
        if (prev.Any(s => s == streamInfo))
            return;

        // Enforce single screencaster per chat — but allow the same author to replace
        // their own prior screencast (mints a new StreamId on every reconnect / pipeline
        // restart; the stale one gets evicted by the same-author/same-kind loop below).
        if (streamInfo.StreamKind == StreamKind.Screencast) {
            var hasForeignScreencast = prev
                .Any(s => s.StreamKind == StreamKind.Screencast
                    && s.AuthorId != streamInfo.AuthorId
                    && s.StreamId != streamInfo.StreamId);
            if (hasForeignScreencast)
                throw new InvalidOperationException("Another screencast is already active in this chat.");
        }

        // Enforce webcam stream cap
        if (streamInfo.StreamKind == StreamKind.Webcam) {
            var webcamCount = prev
                .Count(s => s.StreamKind == StreamKind.Webcam && s.StreamId != streamInfo.StreamId);
            if (webcamCount >= Constants.Video.MaxWebcamStreamsPerChat)
                throw new VideoStreamLimitExceededException(chatId);
        }

        var next = new List<VideoStreamInfo>(prev.Count + 1);
        foreach (var existing in prev) {
            if (existing.StreamId == streamInfo.StreamId)
                continue;

            if (existing.AuthorId == streamInfo.AuthorId
                && existing.StreamKind == streamInfo.StreamKind) {
                Log.LogWarning("Register: evicting stale stream {OldStreamId} for author {AuthorId} (replaced by {NewStreamId})",
                    existing.StreamId, streamInfo.AuthorId, streamInfo.StreamId);
                await _streams.Remove(chatId.Value, existing.StreamId.Value).ConfigureAwait(false);
            }
            else
                next.Add(existing);
        }
        next.Add(streamInfo);

        Log.LogWarning("RegisterActiveStream({ChatId}): #{StreamId}, AuthorId={AuthorId}, StreamKind={StreamKind}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId, streamInfo.StreamKind);
        await _streams.Set(chatId.Value, streamInfo.StreamId.Value, streamInfo).ConfigureAwait(false);
        await primer.Prime(new ApiArray<VideoStreamInfo>(next), cancellationToken).ConfigureAwait(false);

        _ = BackgroundTask.Run(() => EvaluateStreamPriority(chatId, CancellationToken.None), CancellationToken.None);
    }

    public virtual async Task Unregister(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);

        using var isolation = Computed.BeginIsolation();
        using var primer = await _listRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var prev = await List(chatId, cancellationToken).ConfigureAwait(false);
        var next = prev.Where(info => info.StreamId != streamId).ToApiArray();
        if (next.Count == prev.Count)
            return;

        Log.LogWarning("UnregisterActiveStream({ChatId}): #{StreamId}", chatId, streamId);
        await _streams.Remove(chatId.Value, streamId.Value).ConfigureAwait(false);
        await primer.Prime(next, cancellationToken).ConfigureAwait(false);

        _ = BackgroundTask.Run(() => EvaluateStreamPriority(chatId, CancellationToken.None), CancellationToken.None);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(chatId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var allMembers = await SafeGetAll(_members, chatId).ConfigureAwait(false);
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
        await _members.Set(chatId.Value, sessionId, memberInfo).ConfigureAwait(false);
        var allMembers = await SafeGetAll(_members, chatId).ConfigureAwait(false);
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
        var removed = await _members.Remove(chatId.Value,sessionId).ConfigureAwait(false);
        if (removed) {
            var allMembers = await SafeGetAll(_members, chatId).ConfigureAwait(false);
            var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
            var chatState = GetChatState(chatId);
            chatState.RecomputeCodecs(activeMembers);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
        }
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ApiArray<VideoStreamInfo>> ListRaw(ChatId chatId, CancellationToken cancellationToken)
    {
        if (_listRawPrimer.TryUsePrimed(chatId, out var primed))
            return primed;

        var streams = await SafeGetAll(_streams, chatId).ConfigureAwait(false);
        return streams.Values.ToApiArray();
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
                await _members.Remove(chatId.Value,sessionId).ConfigureAwait(false);

            Log.LogDebug("CleanupStaleMembers({ChatId}): removed {Count} stale member(s)", chatId, staleSessionIds.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CleanupStaleMembers({ChatId}): failed to remove stale _members", chatId);
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
        await _streams.RemoveHashMap(entry.Key.Value).ConfigureAwait(false);
        await _members.RemoveHashMap(entry.Key.Value).ConfigureAwait(false);
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
            _ = ListRaw(chatId, default);
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
