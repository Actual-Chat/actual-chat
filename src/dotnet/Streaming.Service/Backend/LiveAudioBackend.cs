using ActualChat.Live;
using ActualChat.Redis;
using ActualLab.Locking;
using ActualLab.Redis;
using ActualLab.Versioning;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service implementation for managing active live audio streams in chats.
/// </summary>
public partial class LiveAudioBackend : ShardComputeService, ILiveAudioBackend
{
    private static readonly TimeSpan MinInvDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StreamTtl = Constants.Audio.MaxStreamDuration;
    private static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(5);

    private readonly RedisScope<State> _redisScope;
    private readonly AsyncLockSet<ChatId> _changeLocks = new(LockReentryMode.CheckedFail);
    private readonly VersionedComputeMethodPrimer<ChatId, long, State> _listRawPrimer;

    private IChatsBackend ChatsBackend { get; }
    private VersionGenerator<long> VersionGenerator { get; }

    public LiveAudioBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        VersionGenerator = services.GetRequiredService<VersionGenerator<long>>();
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        _redisScope = new RedisScope<State>(redisDb, "live-audio:state", Log) {
            DefaultTtl = KeyTtl,
        };
        _listRawPrimer = new VersionedComputeMethodPrimer<ChatId, long, State>(ListRaw);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveAudioStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        if (state.Streams.Count == 0)
            return default;

        var meshState = await MeshWatcher.State.Use(cancellationToken).ConfigureAwait(false);
        return state.Streams.WhereAlive(meshState, static info => StreamId.Parse(info.StreamId)).ToApiArray();
    }

    public virtual async Task Register(ChatId chatId, LiveAudioStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        if (streamInfo.BeginsAt + StreamTtl <= now) {
            Log.LogWarning("Register: ignoring already-expired stream {StreamId} for author {AuthorId} (BeginsAt = {BeginsAt})",
                streamInfo.StreamId, streamInfo.AuthorId, streamInfo.BeginsAt);
            return;
        }

        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var prevState = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        var streams = new List<LiveAudioStreamInfo>(prevState.Streams.Count + 1);
        foreach (var prevStreamInfo in prevState.Streams) {
            if (prevStreamInfo.StreamId == streamInfo.StreamId)
                return; // Already registered
            if (prevStreamInfo.AuthorId == streamInfo.AuthorId) {
                Log.LogWarning("Register: evicting stale stream {OldStreamId} for author {AuthorId} (replaced by {NewStreamId})",
                    prevStreamInfo.StreamId, streamInfo.AuthorId, streamInfo.StreamId);
                continue;
            }
            streams.Add(prevStreamInfo);
        }
        streams.Add(streamInfo);
        streams.Sort(static (a, b) => string.CompareOrdinal(a.StreamId, b.StreamId));

        var nextState = new State(
            VersionGenerator.NextVersion(prevState.Version),
            new ApiArray<LiveAudioStreamInfo>(streams));
        await _redisScope.Set(chatId.Value, nextState).ConfigureAwait(false);
        await _listRawPrimer.Prime(chatId, nextState.Version, nextState, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task Unregister(ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);

        var prev = await ListRaw(chatId, cancellationToken).ConfigureAwait(false);
        var nextStreams = prev.Streams.Where(info => info.StreamId != streamId).ToApiArray();
        if (nextStreams.Count == prev.Streams.Count)
            return;

        var nextState = new State(VersionGenerator.NextVersion(prev.Version), nextStreams);
        await _redisScope.Set(chatId.Value, nextState).ConfigureAwait(false);
        await _listRawPrimer.Prime(chatId, nextState.Version, nextState, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<State> ListRaw(ChatId chatId, CancellationToken cancellationToken)
    {
        // Fusion keeps Computed.Current set only for the synchronous prefix of a compute
        // method, so capture it here — reading it past an await throws "Computed.Current is null".
        var computed = Computed.GetCurrent();
        var now = Clocks.SystemClock.Now;
        if (_listRawPrimer.TryUsePrimed(chatId, out var primed))
            return WithAutoInvalidation(primed);

        try {
            var state = await _redisScope.Get(chatId.Value).ConfigureAwait(false);
            if (state is not null) {
                var alive = state.Streams.Where(info => info.BeginsAt + StreamTtl > now).ToApiArray();
                if (alive.Count != state.Streams.Count)
                    state = state with { Streams = alive };
                return WithAutoInvalidation(state);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to read streams from Redis for chat #{ChatId}, falling back to ChatsBackend", chatId);
        }

        // Fallback: reconstruct from ChatsBackend.ListEntries
        var cutoff = now - Constants.Chat.MaxEntryDuration;
        var entries = await ChatsBackend.ListEntries(chatId, cutoff, cancellationToken).ConfigureAwait(false);
        var byStreamId = new Dictionary<string, LiveAudioStreamInfo>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (entry.Audio is { StreamId.Length: > 0 } liveAudio
                && !MediaId.TryParse(liveAudio.StreamId, out _)) {
                var streamInfo = new LiveAudioStreamInfo {
                    ChatId = chatId,
                    AuthorId = entry.AuthorId,
                    StreamId = liveAudio.StreamId,
                    BeginsAt = entry.BeginsAt,
                    SourceBeginsAt = entry.BeginsAt,
                };
                byStreamId.TryAdd(streamInfo.StreamId, streamInfo);
            }

        byStreamId.RemoveAll((_, info) => info.BeginsAt + StreamTtl <= now);
        var recovered = byStreamId.Values
            .OrderBy(info => info.StreamId, StringComparer.Ordinal)
            .ToApiArray();
        var recoveredState = new State(VersionGenerator.NextVersion(), recovered);

        try {
            await _redisScope.Set(chatId.Value, recoveredState).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to persist reconstructed state to Redis for chat #{ChatId}", chatId);
        }
        return WithAutoInvalidation(recoveredState);

        State WithAutoInvalidation(State state) {
            if (state.Streams.Count == 0)
                return state;

            var minBeginsAt = state.Streams.Min(x => x.BeginsAt);
            var delay = (minBeginsAt + StreamTtl - now + MinInvDelay).Clamp(MinInvDelay, StreamTtl);
            computed.Invalidate(delay);
            return state;
        }
    }

    // Nested types

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
    public sealed partial record State(
        [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long Version,
        [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] ApiArray<LiveAudioStreamInfo> Streams);
}
