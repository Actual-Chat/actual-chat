using ActualChat.Mesh;
using ActualChat.Video;
using ActualLab.Redis;
using ActualLab.Rpc;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend : ShardComputeService, ILiveVideoBackend
{
    private static readonly TimeSpan RedisTtl = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<ChatId, ChatState> _chatStates = new();

    private RedisLiveStateStore<VideoStreamInfo> StreamsStore { get; }
    private RedisLiveStateStore<VideoStreamMemberInfo> MembersStore { get; }
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private new ILogger Log => field ??= Services.LogFor(GetType());

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        var log = services.LogFor(GetType());
        StreamsStore = new RedisLiveStateStore<VideoStreamInfo>(redisDb, "live-video:streams", RedisTtl, log);
        MembersStore = new RedisLiveStateStore<VideoStreamMemberInfo>(redisDb, "live-video:members", RedisTtl, log);

        var stopToken = ShardOwner.StopToken;
        foreach (var shardIndex in ShardScheme.ShardIndexes) {
            var shardIndexCopy = shardIndex;
            var shardState = ShardOwner.States[shardIndex].Value;
            _ = Task.Run(async () => {
                while (true) {
                    shardState = await shardState.WhenNext(stopToken).ConfigureAwait(false);
                    if (shardState.OwnershipStatus == ShardOwnershipStatus.MappedToOtherNode)
                        PurgeShard(shardIndexCopy);
                }
            }, CancellationToken.None);
        }
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<VideoStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await SafeGetAll(StreamsStore, chatId).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        var meshState = MeshWatcher.State.Value;
        List<string>? deadStreamIds = null;

        var liveStreams = new List<VideoStreamInfo>(streams.Count);
        foreach (var (key, info) in streams) {
            var node = meshState[info.StreamId.NodeRef];
            if (node is { State: MeshNodeState.Online })
                liveStreams.Add(info);
            else {
                deadStreamIds ??= new();
                deadStreamIds.Add(key);
                Log.LogWarning(
                    "ListActiveStreams({ChatId}): filtering stale stream {StreamId} (node {NodeRef} is dead/unknown)",
                    chatId, info.StreamId, info.StreamId.NodeRef);
            }
        }

        // Fire-and-forget cleanup of dead entries from Redis
        if (deadStreamIds != null)
            _ = CleanupDeadStreams(chatId, deadStreamIds);

        return new(liveStreams);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await SafeGetAll(StreamsStore, chatId).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        var meshState = MeshWatcher.State.Value;
        return streams.Values
            .Where(s => meshState[s.StreamId.NodeRef] is { State: MeshNodeState.Online })
            .Select(s => s.AuthorId)
            .Distinct()
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var allMembers = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        return activeMembers.Count;
    }

    public virtual async Task<RpcStream<VideoStreamInfo>> Observe(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, allowReconnect: false);
    }

    public virtual async Task Register(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        Log.LogWarning("RegisterActiveStream({ChatId}): #{StreamId}, AuthorId={AuthorId}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId);
        var success = await StreamsStore.SetField(chatId, streamInfo.StreamId.Value, streamInfo).ConfigureAwait(false);
        if (success) {
            var chatState = GetChatState(chatId);
            chatState.PublishNewStream(streamInfo);
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
    }

    public virtual async Task Unregister(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        Log.LogWarning("UnregisterActiveStream({ChatId}): #{StreamId}", chatId, streamId);
        var removed = await StreamsStore.RemoveField(chatId, streamId.Value).ConfigureAwait(false);
        if (removed) {
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        var allMembers = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        var chatState = GetChatState(chatId);
        chatState.RecomputeAndPublishCodecs(activeMembers);
        return chatState.GetCurrentSupportedDecoderCodecs();
    }

    public virtual async Task<RpcStream<ApiArray<string>>> ObserveSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        // NOTE(AY): It clearly doesn't support shard relocation
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveSupportedDecoderCodecs(linkedCts.Token);
        return RpcStream.New(observations, allowReconnect: false);
    }

    public virtual async Task RegisterMember(ChatId chatId, string sessionId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken)
    {
        var memberInfo = new VideoStreamMemberInfo(supportedDecoderCodecs, DateTime.UtcNow.Ticks);
        var success = await MembersStore.SetField(chatId, sessionId, memberInfo).ConfigureAwait(false);
        if (success) {
            var allMembers = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
            var (activeMembers, staleKeys) = FilterStaleMembers(chatId, allMembers);

            Log.LogDebug("RegisterVideoStreamMember({ChatId}): session={SessionId}, codecs=[{Codecs}], active={Active}, stale={Stale}",
                chatId, sessionId, string.Join(", ", supportedDecoderCodecs), activeMembers.Count, staleKeys?.Count ?? 0);

            var chatState = GetChatState(chatId);
            chatState.RecomputeAndPublishCodecs(activeMembers);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
        }
    }

    public virtual async Task UnregisterMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        var removed = await MembersStore.RemoveField(chatId, sessionId).ConfigureAwait(false);
        if (removed) {
            var allMembers = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
            var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
            var chatState = GetChatState(chatId);
            chatState.RecomputeAndPublishCodecs(activeMembers);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
        }
    }

    // Private methods

    private static readonly TimeSpan MemberStalenessThreshold = TimeSpan.FromSeconds(90);

    private (Dictionary<string, ApiArray<string>> Active, List<string>? StaleKeys) FilterStaleMembers(
        ChatId chatId,
        Dictionary<string, VideoStreamMemberInfo> allMembers)
    {
        var cutoff = DateTime.UtcNow.Add(-MemberStalenessThreshold).Ticks;
        var active = new Dictionary<string, ApiArray<string>>(allMembers.Count, StringComparer.Ordinal);
        List<string>? staleKeys = null;

        foreach (var (sessionId, info) in allMembers) {
            if (info.RegisteredAtTicks >= cutoff) {
                active[sessionId] = info.SupportedDecoderCodecs;
            }
            else {
                staleKeys ??= new();
                staleKeys.Add(sessionId);
            }
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
                await MembersStore.RemoveField(chatId, sessionId).ConfigureAwait(false);

            Log.LogDebug("CleanupStaleMembers({ChatId}): removed {Count} stale member(s)", chatId, staleSessionIds.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CleanupStaleMembers({ChatId}): failed to remove stale members", chatId);
        }
    }

    private async Task CleanupDeadStreams(ChatId chatId, List<string> deadStreamIds)
    {
        try {
            foreach (var id in deadStreamIds) {
                var streamId = StreamId.Parse(id);
                await Unregister(chatId, streamId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CleanupDeadStreams({ChatId}): failed to remove stale streams", chatId);
        }
    }

    private async Task<Dictionary<string, TValue>> SafeGetAll<TValue>(RedisLiveStateStore<TValue> store, ChatId chatId)
    {
        try {
            return await store.GetAll(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read video state from Redis for chat {ChatId}, returning empty", chatId);
            return new Dictionary<string, TValue>(StringComparer.Ordinal);
        }
    }

    private ChatState GetChatState(ChatId chatId)
        => _chatStates.GetOrAdd(chatId, static (id, self) => new ChatState(self, id), this);

    private void PurgeShard(int shardIndex)
    {
        var chatIdsToRemove = _chatStates.Keys
            .Where(chatId => ShardScheme.GetShardIndex(chatId) == shardIndex)
            .ToList();

        foreach (var chatId in chatIdsToRemove) {
            if (!_chatStates.TryRemove(chatId, out var chatState))
                continue;

            _ = Task.Run(async () => {
                await StreamsStore.DeleteKey(chatId).ConfigureAwait(false);
                await MembersStore.DeleteKey(chatId).ConfigureAwait(false);
            });

            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
            chatState.Complete(RpcRerouteException.MustReroute());
        }
    }

    private void InvalidateListActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = List(chatId, default);
    }

    private void InvalidateGetVideoStreamingAuthorIds(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetVideoStreamingAuthorIds(chatId, default);
    }

    private void InvalidateGetVideoStreamMemberCount(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetVideoStreamMemberCount(chatId, default);
    }

    private void InvalidateGetSupportedDecoderCodecs(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetSupportedDecoderCodecs(chatId, default);
    }
}
