using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend : ShardComputeService, ILiveVideoBackend
{
    private readonly ConcurrentDictionary<ChatId, ChatState> _chatStates = new();

    private new ILogger Log => field ??= Services.LogFor(GetType());

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.VideoBackend)
    {
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
    public virtual Task<ApiArray<VideoStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        var result = chatState.ListActiveStreams();
        Log.LogWarning("ListActiveStreams({ChatId}): returning {Count} streams", chatId, result.Count);
        return Task.FromResult(result);
    }

    // [ComputeMethod]
    public virtual Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        var result = chatState.GetStreamingAuthorIds();
        Log.LogWarning("GetVideoStreamingAuthorIds({ChatId}): returning {Count} authors: [{Authors}]",
            chatId, result.Length, string.Join(", ", result));
        return Task.FromResult(result);
    }

    // [ComputeMethod]
    public virtual Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        return Task.FromResult(chatState.GetMemberCount());
    }

    public virtual async Task<RpcStream<VideoStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, isReconnectable: false);
    }

    public virtual Task RegisterActiveStream(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        Log.LogWarning("RegisterActiveStream({ChatId}): StreamId={StreamId}, AuthorId={AuthorId}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId);
        var chatState = GetChatState(chatId);
        if (chatState.RegisterStream(streamInfo)) {
            Log.LogWarning("RegisterActiveStream({ChatId}): Stream registered, invalidating computed values", chatId);
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
        else {
            Log.LogWarning("RegisterActiveStream({ChatId}): Stream already registered (duplicate)", chatId);
        }
        return Task.CompletedTask;
    }

    public virtual Task UnregisterActiveStream(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        Log.LogWarning("UnregisterActiveStream({ChatId}): StreamId={StreamId}", chatId, streamId);
        if (!_chatStates.TryGetValue(chatId, out var chatState))
            return Task.CompletedTask;

        if (chatState.UnregisterStream(streamId)) {
            Log.LogWarning("UnregisterActiveStream({ChatId}): Stream removed, invalidating", chatId);
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
        return Task.CompletedTask;
    }

    public virtual Task RegisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        if (chatState.RegisterMember(sessionId))
            InvalidateGetVideoStreamMemberCount(chatId);
        return Task.CompletedTask;
    }

    public virtual Task UnregisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        if (!_chatStates.TryGetValue(chatId, out var chatState))
            return Task.CompletedTask;

        if (chatState.UnregisterMember(sessionId))
            InvalidateGetVideoStreamMemberCount(chatId);
        return Task.CompletedTask;
    }

    // Private methods

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

            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
            InvalidateGetVideoStreamMemberCount(chatId);
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
}
