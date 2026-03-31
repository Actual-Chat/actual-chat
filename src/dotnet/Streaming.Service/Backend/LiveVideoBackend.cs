using ActualChat.Mesh;
using ActualChat.Video;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend : ShardComputeService, ILiveVideoBackend
{
    private static readonly TimeSpan RedisTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ChatStateTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<ChatId, ExpiringEntry<ChatId, ChatState>> _chatStates = new();

    private RedisHashStore<VideoStreamInfo> StreamsStore { get; }
    private RedisHashStore<VideoStreamMemberInfo> MembersStore { get; }
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private new ILogger Log => field ??= Services.LogFor(GetType());

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.LiveBackend)
    {
        var redisDb = services.GetRequiredService<RedisDb<StreamingContext>>();
        var log = services.LogFor(GetType());
        StreamsStore = new RedisHashStore<VideoStreamInfo>(redisDb, "live-video:streams", RedisTtl, log);
        MembersStore = new RedisHashStore<VideoStreamMemberInfo>(redisDb, "live-video:members", RedisTtl, log);
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
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var allMembers = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        return activeMembers.Count;
    }

    public virtual async Task Register(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);

        // Enforce single screencaster per chat
        if (streamInfo.StreamKind == StreamKind.Screencast) {
            var existingStreams = await SafeGetAll(StreamsStore, chatId).ConfigureAwait(false);
            var hasExistingScreencast = existingStreams.Values
                .Any(s => s.StreamKind == StreamKind.Screencast && s.StreamId != streamInfo.StreamId);
            if (hasExistingScreencast)
                throw new InvalidOperationException("Another screencast is already active in this chat.");
        }

        Log.LogWarning("RegisterActiveStream({ChatId}): #{StreamId}, AuthorId={AuthorId}, StreamKind={StreamKind}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId, streamInfo.StreamKind);
        var success = await StreamsStore.SetField(chatId, streamInfo.StreamId.Value, streamInfo).ConfigureAwait(false);
        if (success) {
            InvalidateListActiveStreams(chatId);
            if (streamInfo.StreamKind == StreamKind.Screencast)
                InvalidateHasActiveScreencast(chatId);
        }
    }

    public virtual async Task Unregister(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        BumpChatEntry(chatId);
        Log.LogWarning("UnregisterActiveStream({ChatId}): #{StreamId}", chatId, streamId);
        var removed = await StreamsStore.RemoveField(chatId, streamId.Value).ConfigureAwait(false);
        if (removed) {
            InvalidateListActiveStreams(chatId);
            InvalidateHasActiveScreencast(chatId);
        }
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasActiveScreencast(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await List(chatId, cancellationToken).ConfigureAwait(false);
        return streams.Any(s => s.StreamKind == StreamKind.Screencast);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<string>> GetSupportedCodecs(ChatId chatId, CancellationToken cancellationToken)
    {
        var allMembers = await SafeGetAll(MembersStore, chatId).ConfigureAwait(false);
        var (activeMembers, _) = FilterStaleMembers(chatId, allMembers);
        var chatState = GetChatState(chatId);
        chatState.RecomputeCodecs(activeMembers);
        return chatState.GetCurrentSupportedDecoderCodecs();
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
            chatState.RecomputeCodecs(activeMembers);
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
            chatState.RecomputeCodecs(activeMembers);
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

    private async Task<Dictionary<string, TValue>> SafeGetAll<TValue>(RedisHashStore<TValue> store, ChatId chatId)
    {
        try {
            return await store.GetAll(chatId).ConfigureAwait(false);
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
        await StreamsStore.DeleteKey(entry.Key).ConfigureAwait(false);
        await MembersStore.DeleteKey(entry.Key).ConfigureAwait(false);
        InvalidateListActiveStreams(entry.Key);
        InvalidateGetVideoStreamMemberCount(entry.Key);
        InvalidateGetSupportedDecoderCodecs(entry.Key);
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

    private void InvalidateHasActiveScreencast(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = HasActiveScreencast(chatId, default);
    }
}
