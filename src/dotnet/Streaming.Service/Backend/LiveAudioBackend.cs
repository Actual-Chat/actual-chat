using ActualChat.Chat;
using ActualChat.Live;
using ActualLab.Redis;
using ActualLab.Rpc;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for managing active live audio streams in chats.
/// </summary>
public partial class LiveAudioBackend : ShardComputeService, ILiveAudioBackend
{
    private readonly ConcurrentDictionary<ChatId, ChatState> _chatStates = new();

    private IChatsBackend ChatsBackend { get; }
    private MomentClock ServerClock { get; }
    private RedisLiveStateStore<LiveStreamInfo> StreamsStore { get; }

    public LiveAudioBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        ServerClock = services.Clocks().ServerClock;
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        StreamsStore = new RedisLiveStateStore<LiveStreamInfo>(redisDb, "live-audio:streams", Constants.Audio.MaxStreamDuration*2, Log);

        var stopToken = ShardOwner.StopToken;
        foreach (var shardIndex in ShardScheme.ShardIndexes) {
            var shardIndexCopy = shardIndex; // Capture for closure
            var shardState = ShardOwner.States[shardIndex].Value;
            // Clear streams only when THIS shard loses ownership
            _ = Task.Run(async () => {
                while (true) {
                    shardState = await shardState.WhenNext(stopToken).ConfigureAwait(false);
                    // Only clear when we lose ownership (not when gaining or during startup)
                    if (shardState.OwnershipStatus == ShardOwnershipStatus.MappedToOtherNode)
                        PurgeShard(shardIndexCopy);
                }
            }, CancellationToken.None);
        }
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await ReadStreamsFromRedis(chatId).ConfigureAwait(false);
        return new(streams.Values);
    }

    public virtual async Task<RpcStream<LiveStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        // linkedCts will be either cancelled (most likely) or GC collected - that's why we don't dispose it explicitly
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, allowReconnect: false);
    }

    public virtual async Task RegisterActiveStream(ChatId chatId, LiveStreamInfo activeStream, CancellationToken cancellationToken)
    {
        var success = await StreamsStore.SetField(chatId, activeStream.StreamId, activeStream).ConfigureAwait(false);
        if (success) {
            var chatState = GetChatState(chatId);
            chatState.PublishNewStream(activeStream);
            InvalidateListActiveStreams(chatId);
        }
    }

    public virtual async Task UnregisterActiveStream(ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        var removed = await StreamsStore.RemoveField(chatId, streamId).ConfigureAwait(false);
        if (removed)
            InvalidateListActiveStreams(chatId);
    }

    // Private methods

    private async Task<Dictionary<string, LiveStreamInfo>> ReadStreamsFromRedis(ChatId chatId)
    {
        try {
            return await StreamsStore.GetAll(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read audio streams from Redis for chat {ChatId}, falling back to ChatsBackend", chatId);
        }

        // Fallback: reconstruct from ChatsBackend.ListEntries
        var result = new Dictionary<string, LiveStreamInfo>(StringComparer.Ordinal);
        var minBeginsAt = ServerClock.Now - Constants.Chat.MaxEntryDuration;
        var entries = await ChatsBackend.ListEntries(chatId, minBeginsAt, default).ConfigureAwait(false);

        foreach (var entry in entries)
            if (entry.Audio is { StreamId.Length: > 0 } liveAudio
                && !MediaId.TryParse(liveAudio.StreamId, out _)) {
                var streamInfo = new LiveStreamInfo {
                    ChatId = chatId,
                    AuthorId = entry.AuthorId,
                    StreamId = liveAudio.StreamId,
                    BeginsAt = entry.BeginsAt,
                };
                result.TryAdd(streamInfo.StreamId, streamInfo);
            }

        // Write recovered entries to Redis for future restarts
        foreach (var (streamId, info) in result)
            await StreamsStore.SetField(chatId, streamId, info).ConfigureAwait(false);

        return result;
    }

    private ChatState GetChatState(ChatId chatId)
        => _chatStates.GetOrAdd(chatId, static (id, self) => new ChatState(self, id), this);

    private void PurgeShard(int shardIndex)
    {
        // Only clear streams for chats that belong to this shard
        var chatIdsToRemove = _chatStates.Keys
            .Where(chatId => ShardScheme.GetShardIndex(chatId) == shardIndex)
            .ToList();

        foreach (var chatId in chatIdsToRemove) {
            if (!_chatStates.TryRemove(chatId, out var chatState))
                continue;

            _ = Task.Run(async () => {
                await StreamsStore.DeleteKey(chatId).ConfigureAwait(false);
            });

            InvalidateListActiveStreams(chatId);
            chatState.Complete(RpcRerouteException.MustReroute());
        }
    }

    private void InvalidateListActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListActiveStreams(chatId, default);
    }
}
