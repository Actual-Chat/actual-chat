using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for managing active live audio streams in chats.
/// </summary>
public class LiveAudioBackend : ShardComputeService, ILiveAudioBackend
{
    private static readonly TimeSpan StreamTtl = Constants.Audio.MaxStreamDuration * 2; // Ok to keep it a bit longer
    private static readonly TimeSpan HashTtl = TimeSpan.FromHours(1);

    private RedisMultiHashMap<LiveStreamInfo> Streams { get; }
    private LockingComputeMethodPrimer<ChatId, ApiArray<LiveStreamInfo>> ListRawPrimer { get; }

    private IChatsBackend ChatsBackend { get; }

    public LiveAudioBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        Streams = new RedisMultiHashMap<LiveStreamInfo>(redisDb, "live-audio:streams", Log) {
            HashTtl = HashTtl,
            DefaultFieldTtl = StreamTtl,
        };
        ListRawPrimer = new LockingComputeMethodPrimer<ChatId, ApiArray<LiveStreamInfo>>(ListRaw);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
            return default;

        var meshState = await MeshWatcher.State.Use(cancellationToken).ConfigureAwait(false);
        return streams.WhereAlive(meshState, static info => StreamId.Parse(info.StreamId)).ToApiArray();
    }

    public virtual async Task Register(ChatId chatId, LiveStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var primer = await ListRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var prev = await List(chatId, cancellationToken).ConfigureAwait(false);
        var next = new List<LiveStreamInfo>(prev.Count + 1);
        foreach (var info in prev) {
            if (info.StreamId == streamInfo.StreamId)
                continue;

            if (info.AuthorId == streamInfo.AuthorId) {
                Log.LogWarning("Register: evicting stale stream {OldStreamId} for author {AuthorId} (replaced by {NewStreamId})",
                    info.StreamId, streamInfo.AuthorId, streamInfo.StreamId);
                await Streams.Remove(chatId.Value, info.StreamId).ConfigureAwait(false);
            }
            else
                next.Add(info);
        }
        next.Add(streamInfo);
        await Streams.Set(chatId.Value, streamInfo.StreamId, streamInfo).ConfigureAwait(false);
        await primer.Prime(new ApiArray<LiveStreamInfo>(next), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task Unregister(ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var primer = await ListRawPrimer.LockAndPrepare(chatId, cancellationToken).ConfigureAwait(false);

        var prev = await List(chatId, cancellationToken).ConfigureAwait(false);
        var next = prev.Where(info => info.StreamId != streamId).ToApiArray();
        if (next.Count == prev.Count)
            return;

        await Streams.Remove(chatId.Value, streamId).ConfigureAwait(false);
        await primer.Prime(next, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ApiArray<LiveStreamInfo>> ListRaw(ChatId chatId, CancellationToken cancellationToken)
    {
        if (ListRawPrimer.TryUsePrimed(chatId, out var primed))
            return primed;

        try {
            var map = await Streams.GetHashMap(chatId.Value).ConfigureAwait(false);
            map.RemoveAll(static (_, v) => v is null);
            return map.Values.ToApiArray()!;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read streams from Redis for chat #{ChatId}, falling back to ChatsBackend", chatId);
        }

        // Fallback: reconstruct from ChatsBackend.ListEntries
        var result = new Dictionary<string, LiveStreamInfo>(StringComparer.Ordinal);
        var minBeginsAt = Clocks.SystemClock.Now - Constants.Chat.MaxEntryDuration;
        var entries = await ChatsBackend.ListEntries(chatId, minBeginsAt, cancellationToken).ConfigureAwait(false);
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
        foreach (var info in result.Values)
            await Streams.Set(chatId.Value, info.StreamId, info).ConfigureAwait(false);

        return new ApiArray<LiveStreamInfo>(result.Values);
    }
}
