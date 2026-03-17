using ActualChat.Chat;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// RPC service providing multiplexed historical audio replay streams.
/// </summary>
public class ReplayStreams(IServiceProvider services) : IReplayStreams
{
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<(Session, ChatId), ReplayStreamMuxer> _muxers = new();

    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILogger Log => field ??= Services.LogFor<ReplayStreams>();

    public async Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();

        ReplayStreamMuxer muxer;
        var key = (session, chatId);
        lock (_lock) {
            if (_muxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync();

            muxer = new ReplayStreamMuxer(Services, session, chatId, startAt, offset);
            _muxers[key] = muxer;
        }

        var stream = ToAsyncEnumerable(muxer.Output, key, cancellationToken);
        return RpcStream.New(stream, allowReconnect: false);
    }

    // Private methods

    private async IAsyncEnumerable<LiveStreamItem> ToAsyncEnumerable(
        ChannelReader<LiveStreamItem> reader,
        (Session, ChatId) key,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            if (_muxers.TryRemove(key, out var muxer))
                await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
