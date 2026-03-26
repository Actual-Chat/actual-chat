using ActualChat.Live;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for managing active live audio streams in chats.
/// </summary>
public class LiveAudioBackend : ShardComputeService, ILiveAudioBackend
{
    private static readonly TimeSpan ChatEntryTtl = Constants.Audio.MaxStreamDuration * 2;

    private readonly ConcurrentDictionary<ChatId, ExpiringEntry<ChatId, ChatId>> _activeChatEntries = new();

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
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);
        var streams = await ReadStreamsFromRedis(chatId).ConfigureAwait(false);
        return new(streams.Values);
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
            var e = ExpiringEntry.New(self._activeChatEntries, id, id);
            e.SetDisposer(self.OnChatEntryExpired);
            e.BumpExpiresAt(ChatEntryTtl);
            e.BeginExpire();
            return e;
        }, this);
        entry.BumpExpiresAt(ChatEntryTtl);
    }

    private async ValueTask OnChatEntryExpired(ExpiringEntry<ChatId, ChatId> entry)
    {
        Log.LogDebug("OnChatEntryExpired({ChatId}): cleaning up Redis", entry.Key);
        await StreamsStore.DeleteKey(entry.Key).ConfigureAwait(false);
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

    private void InvalidateListStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = List(chatId, default);
    }
}
