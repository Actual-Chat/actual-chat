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
    private RedisLiveStateStore<ApiArray<string>> MembersStore { get; }
    private new ILogger Log => field ??= Services.LogFor(GetType());

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        var log = services.LogFor(GetType());
        StreamsStore = new RedisLiveStateStore<VideoStreamInfo>(redisDb, "live-video:streams", RedisTtl, log);
        MembersStore = new RedisLiveStateStore<ApiArray<string>>(redisDb, "live-video:members", RedisTtl, log);

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
    public virtual async Task<ApiArray<VideoStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await SafeGetAll(StreamsStore, chatId).ConfigureAwait(false);
        return new(streams.Values);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await SafeGetAll(StreamsStore, chatId).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        return streams.Values
            .Select(s => s.AuthorId)
            .Distinct()
            .ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var members = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
        return members.Count;
    }

    public virtual async Task<RpcStream<VideoStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, allowReconnect: false);
    }

    public virtual async Task RegisterActiveStream(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        Log.LogWarning("RegisterActiveStream({ChatId}): StreamId={StreamId}, AuthorId={AuthorId}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId);
        var success = await StreamsStore.SetField(chatId, streamInfo.StreamId.Value, streamInfo).ConfigureAwait(false);
        if (success) {
            var chatState = GetChatState(chatId);
            chatState.PublishNewStream(streamInfo);
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
    }

    public virtual async Task UnregisterActiveStream(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        Log.LogWarning("UnregisterActiveStream({ChatId}): StreamId={StreamId}", chatId, streamId);
        var removed = await StreamsStore.RemoveField(chatId, streamId.Value).ConfigureAwait(false);
        if (removed) {
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        var members = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
        var chatState = GetChatState(chatId);
        chatState.RecomputeAndPublishCodecs(members);
        return chatState.GetCurrentSupportedDecoderCodecs();
    }

    public virtual async Task<RpcStream<ApiArray<string>>> ObserveSupportedDecoderCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveSupportedDecoderCodecs(linkedCts.Token);
        return RpcStream.New(observations, allowReconnect: false);
    }

    public virtual async Task RegisterVideoStreamMember(ChatId chatId, string sessionId, ApiArray<string> supportedDecoderCodecs, CancellationToken cancellationToken)
    {
        var success = await MembersStore.SetField(chatId, sessionId, supportedDecoderCodecs).ConfigureAwait(false);
        if (success) {
            var members = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
            var chatState = GetChatState(chatId);
            chatState.RecomputeAndPublishCodecs(members);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
        }
    }

    public virtual async Task UnregisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        var removed = await MembersStore.RemoveField(chatId, sessionId).ConfigureAwait(false);
        if (removed) {
            var members = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
            var chatState = GetChatState(chatId);
            chatState.RecomputeAndPublishCodecs(members);
            InvalidateGetVideoStreamMemberCount(chatId);
            InvalidateGetSupportedDecoderCodecs(chatId);
        }
    }

    // Private methods

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
            _ = ListActiveStreams(chatId, default);
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
