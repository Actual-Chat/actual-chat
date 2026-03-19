using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// RPC service providing multiplexed real-time audio streams.
/// </summary>
public class LiveStreams(IServiceProvider services) : ILiveStreams
{
    private readonly Lock _lock = new ();
    private readonly ConcurrentDictionary<(Session, ChatId), LiveStreamMuxer> _muxers = new();

    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILiveAudioBackend LiveBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private ILogger Log => field ??= Services.LogFor<LiveStreams>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> ListActiveStreams(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await LiveBackend.ListActiveStreams(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<LiveStreamItem>> GetLiveStream(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();

        LiveStreamMuxer muxer;
        var key = (session, chatId);
        lock (_lock) { // TODO(AY): Make it more efficient later?
            if (_muxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync(); // No need to await for this here

            muxer = new LiveStreamMuxer(Services, session, chatId, settings);
            _muxers[key] = muxer;
        }

        var stream = ToAsyncEnumerable(muxer.Output, key, cancellationToken);
        return RpcStream.New(stream, allowReconnect: false);
    }

    public async Task ChangeSettings(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();

        if (_muxers.TryGetValue((session, chatId), out var muxer))
            muxer.UpdateConfig(settings);
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
            // Clean up muxer when stream ends
            if (_muxers.TryRemove(key, out var muxer))
                await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
