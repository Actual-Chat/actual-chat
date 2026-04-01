using ActualChat.Live;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for managing active live audio streams in chats.
/// </summary>
public class LiveAudioBackend : ShardComputeService, ILiveAudioBackend
{
    private static readonly TimeSpan RedisTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ChatEntryTtl = Constants.Audio.MaxStreamDuration * 2;

    private readonly ConcurrentDictionary<ChatId, ExpiringEntry<ChatId, Unit>> _activeChatEntries = new();

    private IChatsBackend ChatsBackend { get; }
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private RedisHashStore<LiveStreamInfo> StreamsStore { get; }
    private MomentClock Clock { get; }

    public LiveAudioBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        Clock = services.Clocks().SystemClock;
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        StreamsStore = new RedisHashStore<LiveStreamInfo>(redisDb, "live-audio:streams", RedisTtl, Log);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await ReadStreamsFromRedis(chatId).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        var meshState = await MeshWatcher.State.Use(cancellationToken).ConfigureAwait(false);
        var liveStreams = new List<LiveStreamInfo>(streams.Count);
        List<string>? deadStreamIds = null;
        foreach (var (key, info) in streams) {
            var streamId = StreamId.Parse(info.StreamId);
            var node = meshState[streamId.NodeRef];
            if (node is { State: MeshNodeState.Online })
                liveStreams.Add(info);
            else {
                deadStreamIds ??= new();
                deadStreamIds.Add(key);
            }
        }
        if (deadStreamIds != null)
            _ = BackgroundTask.Run(() => CleanupDeadStreams(chatId, deadStreamIds), CancellationToken.None);

        return new(liveStreams);
    }

    public virtual async Task Register(ChatId chatId, LiveStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);
        var success = await StreamsStore.SetField(chatId, streamInfo.StreamId, streamInfo).ConfigureAwait(false);
        if (success)
            InvalidateListStreams(chatId);
    }

    public virtual async Task Unregister(ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);
        var removed = await StreamsStore.RemoveField(chatId, streamId).ConfigureAwait(false);
        if (removed)
            InvalidateListStreams(chatId);
    }

    // Private methods

    private void BumpChatEntry(ChatId chatId)
    {
        var entry = _activeChatEntries.GetOrAdd(chatId, static (id, self) => {
            var e = ExpiringEntry.New(self._activeChatEntries, id, Unit.Default);
            e.SetDisposer(self.OnChatEntryExpired);
            e.BumpExpiresAt(ChatEntryTtl);
            e.BeginExpire();
            return e;
        }, this);
        entry.BumpExpiresAt(ChatEntryTtl);
    }

    private async ValueTask OnChatEntryExpired(ExpiringEntry<ChatId, Unit> entry)
    {
        Log.LogDebug("OnChatEntryExpired({ChatId}): cleaning up Redis", entry.Key);
        await StreamsStore.DeleteKey(entry.Key).ConfigureAwait(false);
        InvalidateListStreams(entry.Key);
    }

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
        var minBeginsAt = Clock.Now - Constants.Chat.MaxEntryDuration;
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

    private async Task CleanupDeadStreams(ChatId chatId, List<string> deadStreamIds)
    {
        try {
            foreach (var id in deadStreamIds)
                await StreamsStore.RemoveField(chatId, id).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CleanupDeadStreams({ChatId}): failed to remove stale streams", chatId);
        }
    }

    private void InvalidateListStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = List(chatId, default);
    }
}
