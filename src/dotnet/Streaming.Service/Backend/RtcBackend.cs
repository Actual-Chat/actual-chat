using ActualChat.Rtc;
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
            var shardState = ShardOwner.States[shardIndex].Value;
            // Clear streams on any shard ownership state change
            _ = Task.Run(async () => {
                while (true) {
                    shardState = await shardState.WhenNext(stopToken).ConfigureAwait(false);
                    ClearAllStreams();
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

    public virtual Task<RpcStream<RtcStreamInfo>> ObserveNewStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var observations = GetChatStreams(chatId).ObserveNewStreams(cancellationToken);
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

    private void ClearAllStreams()
    {
        foreach (var (_, chatStreams) in _chatStreams)
            chatStreams.Complete();
        _chatStreams.Clear();
    }

    // Nested types

    private sealed class ChatStreamSet
    {
        private readonly ConcurrentDictionary<string, RtcStreamInfo> _streams = new(StringComparer.Ordinal);
        private readonly Channel<RtcStreamInfo> _newStreams = ChannelExt.Create<RtcStreamInfo>(ChannelExt.UnboundedPipeOptions);

        public ApiArray<RtcStreamInfo> GetActiveStreams()
            => new(_streams.Values.ToArray());

        public async IAsyncEnumerable<RtcStreamInfo> ObserveNewStreams(
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
