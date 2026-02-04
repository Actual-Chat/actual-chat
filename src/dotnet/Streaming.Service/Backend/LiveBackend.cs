using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class LiveBackend : ShardComputeService, ILiveBackend
{
    private readonly ConcurrentDictionary<ChatId, ChatStreamSet> _chatStreams = new();

    public LiveBackend(IServiceProvider services)
        : base(services, ShardScheme.AudioBackend)
    {
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
                        ClearAndRerouteStreams(shardIndexCopy);
                }
            }, CancellationToken.None);
        }
    }

    [ComputeMethod]
    public virtual Task<ApiArray<LiveStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatStreams = GetChatStreams(chatId);
        return Task.FromResult(chatStreams.GetActiveStreams());
    }

    public virtual async Task<RpcStream<LiveStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        // linkedCts will be either cancelled (most likely) or GC collected - that's why we don't dispose it explicitly
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var observations = GetChatStreams(chatId).ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, isReconnectable: false);
    }

    public virtual async Task RegisterActiveStream(ChatId chatId, LiveStreamInfo activeStream, CancellationToken cancellationToken)
    {
        var chatStreams = GetChatStreams(chatId);
        if (chatStreams.TryAdd(activeStream)) {
            InvalidateGetActiveStreams(chatId);
            await chatStreams.NotifyNew(activeStream, cancellationToken).ConfigureAwait(false);
        }
    }

    public virtual Task UnregisterActiveStream(ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        if (!_chatStreams.TryGetValue(chatId, out var chatStreams))
            return Task.CompletedTask;
        if (chatStreams.TryRemove(streamId))
            InvalidateGetActiveStreams(chatId);
        return Task.CompletedTask;
    }

    // Private methods

    private ChatStreamSet GetChatStreams(ChatId chatId)
        => _chatStreams.GetOrAdd(chatId, _ => new ChatStreamSet());

    private void InvalidateGetActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListActiveStreams(chatId, default);
    }

    private void ClearAndRerouteStreams(int shardIndex)
    {
        // Only clear streams for chats that belong to this shard
        var chatIdsToRemove = _chatStreams.Keys
            .Where(chatId => ShardScheme.GetShardIndex(chatId) == shardIndex)
            .ToList();

        foreach (var chatId in chatIdsToRemove) {
            if (!_chatStreams.TryRemove(chatId, out var chatStreams))
                continue;

            InvalidateGetActiveStreams(chatId);
            chatStreams.Complete(RpcRerouteException.MustReroute());
        }
    }

    // Nested types

    private sealed class ChatStreamSet
    {
        private readonly ConcurrentDictionary<string, LiveStreamInfo> _streams = new(StringComparer.Ordinal);
        private readonly Channel<LiveStreamInfo> _newStreams = ChannelExt.Create<LiveStreamInfo>(ChannelExt.UnboundedPipeOptions);

        public ApiArray<LiveStreamInfo> GetActiveStreams()
            => new(_streams.Values.ToArray());

        public async IAsyncEnumerable<LiveStreamInfo> ObserveStreams(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Yield all currently active streams first
            foreach (var stream in _streams.Values)
                yield return stream;

            // Then yield new streams as they come
            await foreach (var stream in _newStreams.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return stream;
        }

        public bool TryAdd(LiveStreamInfo stream)
            => _streams.TryAdd(stream.StreamId, stream);

        public bool TryRemove(string streamId)
            => _streams.TryRemove(streamId, out _);

        public ValueTask NotifyNew(LiveStreamInfo stream, CancellationToken cancellationToken)
            => _newStreams.Writer.WriteAsync(stream, cancellationToken);

        public void Complete(Exception? error = null)
            => _newStreams.Writer.TryComplete(error);
    }
}
