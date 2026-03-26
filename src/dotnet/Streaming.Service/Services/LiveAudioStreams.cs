using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// RPC service providing multiplexed real-time audio streams.
/// </summary>
public class LiveAudioStreams(IServiceProvider services) : ILiveAudioStreams
{
    private readonly Lock _lock = new ();
    private readonly ConcurrentDictionary<(Session, ChatId), LiveStreamMuxer> _liveMuxers = new();
    private readonly ConcurrentDictionary<(Session, ChatId), ReplayStreamMuxer> _replayMuxers = new();

    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ILiveAudioBackend LiveBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private ILogger Log => field ??= Services.LogFor<LiveAudioStreams>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> List(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await LiveBackend.List(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<LiveStreamItem>> GetStream(
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
            if (_liveMuxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync(); // No need to await for this here

            muxer = new LiveStreamMuxer(Services, session, chatId, settings);
            _liveMuxers[key] = muxer;
        }

        var stream = ToLiveAsyncEnumerable(key, muxer.Output, cancellationToken);
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

        if (_liveMuxers.TryGetValue((session, chatId), out var muxer))
            muxer.UpdateConfig(settings);
    }

    public async Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();

        ReplayStreamMuxer muxer;
        var key = (session, chatId);
        lock (_lock) {
            if (_replayMuxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync();

            muxer = new ReplayStreamMuxer(Services, session, chatId, startAt, rewindOffset, speed);
            _replayMuxers[key] = muxer;
        }

        var stream = ToReplayAsyncEnumerable(key, muxer.Output, cancellationToken);
        return RpcStream.New(stream, allowReconnect: false);
    }

    // Private methods

    private async IAsyncEnumerable<LiveStreamItem> ToLiveAsyncEnumerable(
        (Session, ChatId) key,
        ChannelReader<LiveStreamItem> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            if (_liveMuxers.TryRemove(key, out var muxer))
                await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<LiveStreamItem> ToReplayAsyncEnumerable(
        (Session, ChatId) key,
        ChannelReader<LiveStreamItem> reader,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            if (_replayMuxers.TryRemove(key, out var muxer))
                await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
