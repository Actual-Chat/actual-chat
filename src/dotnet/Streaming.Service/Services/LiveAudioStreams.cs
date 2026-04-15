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

            muxer = new LiveStreamMuxer(Services, chatId, settings);
            _liveMuxers[key] = muxer;
        }

        var stream = ToLiveAsyncEnumerable(key, muxer, muxer.Output, cancellationToken);
        return new RpcStream<LiveStreamItem>(stream) {
            AllowReconnect = false,
            AckPeriod = Constants.Audio.StreamAckPeriod,
            AckAdvance = Constants.Audio.StreamAckAdvance,
        };
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

        var stream = ToReplayAsyncEnumerable(key, muxer, muxer.Output, cancellationToken);
        return new RpcStream<LiveStreamItem>(stream) {
            AllowReconnect = false,
            AckPeriod = Constants.Audio.StreamAckPeriod,
            AckAdvance = Constants.Audio.StreamAckAdvance,
        };
    }

    // Private methods

    private async IAsyncEnumerable<LiveStreamItem> ToLiveAsyncEnumerable(
        (Session, ChatId) key,
        LiveStreamMuxer originalMuxer,
        ChannelReader<LiveStreamItem> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            // Only remove if the muxer in the dictionary is still the one we started with.
            // A new GetStream call may have replaced it; removing the replacement would be wrong.
            // Use lock to avoid TOCTOU race with GetStream (which also holds _lock).
            bool shouldDispose;
            lock (_lock) {
                shouldDispose = _liveMuxers.TryGetValue(key, out var current)
                    && ReferenceEquals(current, originalMuxer)
                    && _liveMuxers.TryRemove(key, out _);
            }
            if (shouldDispose)
                await originalMuxer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<LiveStreamItem> ToReplayAsyncEnumerable(
        (Session, ChatId) key,
        ReplayStreamMuxer originalMuxer,
        ChannelReader<LiveStreamItem> reader,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            bool shouldDispose;
            lock (_lock) {
                shouldDispose = _replayMuxers.TryGetValue(key, out var current)
                    && ReferenceEquals(current, originalMuxer)
                    && _replayMuxers.TryRemove(key, out _);
            }
            if (shouldDispose)
                await originalMuxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
