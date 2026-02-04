using ActualChat.Rtc;
using ActualChat.Sharding;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class RtcBackend : ShardComputeService, IRtcBackend
{
    private readonly ConcurrentDictionary<ChatId, ChatStreamSet> _chatStreams = new();

    public RtcBackend(IServiceProvider services)
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
                        ClearStreamsForShard(shardIndexCopy);
                }
            }, CancellationToken.None);
        }
    }

    [ComputeMethod]
    public virtual Task<ApiArray<RtcStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatStreams = GetChatStreams(chatId);
        return Task.FromResult(chatStreams.GetActiveStreams());
    }

    public virtual Task<RpcStream<RtcStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var observations = GetChatStreams(chatId).ObserveStreams(cancellationToken);
        return Task.FromResult(RpcStream.New(observations, isReconnectable: false));
    }

    // Internal methods called by StreamingBackend

    internal async ValueTask RegisterActiveStream(RtcStreamInfo activeStream, CancellationToken cancellationToken)
    {
        var chatStreams = GetChatStreams(activeStream.ChatId);
        if (chatStreams.TryAdd(activeStream)) {
            InvalidateGetActiveStreams(activeStream.ChatId);
            await chatStreams.NotifyNew(activeStream, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void UnregisterActiveStream(ChatId chatId, string streamId)
    {
        if (!_chatStreams.TryGetValue(chatId, out var chatStreams))
            return;
        if (chatStreams.TryRemove(streamId))
            InvalidateGetActiveStreams(chatId);
    }

    // Private methods

    private ChatStreamSet GetChatStreams(ChatId chatId)
        => _chatStreams.GetOrAdd(chatId, _ => new ChatStreamSet());

    private void InvalidateGetActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListActiveStreams(chatId, default);
    }

    private void ClearStreamsForShard(int shardIndex)
    {
        // Only clear streams for chats that belong to this shard
        var chatIdsToRemove = _chatStreams.Keys
            .Where(chatId => ShardScheme.GetShardIndex(chatId) == shardIndex)
            .ToList();

        foreach (var chatId in chatIdsToRemove) {
            if (_chatStreams.TryRemove(chatId, out var chatStreams))
                chatStreams.Complete();
        }
    }

    // Nested types

    private sealed class ChatStreamSet
    {
        private readonly ConcurrentDictionary<string, RtcStreamInfo> _streams = new(StringComparer.Ordinal);
        private readonly Channel<RtcStreamInfo> _newStreams = ChannelExt.Create<RtcStreamInfo>(ChannelExt.UnboundedPipeOptions);

        public ApiArray<RtcStreamInfo> GetActiveStreams()
            => new(_streams.Values.ToArray());

        public async IAsyncEnumerable<RtcStreamInfo> ObserveStreams(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Yield all currently active streams first
            foreach (var stream in _streams.Values)
                yield return stream;

            // Then yield new streams as they come
            await foreach (var stream in _newStreams.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return stream;
        }

        public bool TryAdd(RtcStreamInfo stream)
            => _streams.TryAdd(stream.StreamId, stream);

        public bool TryRemove(string streamId)
            => _streams.TryRemove(streamId, out _);

        public ValueTask NotifyNew(RtcStreamInfo stream, CancellationToken cancellationToken)
            => _newStreams.Writer.WriteAsync(stream, cancellationToken);

        public void Complete()
            => _newStreams.Writer.TryComplete();
    }
}
