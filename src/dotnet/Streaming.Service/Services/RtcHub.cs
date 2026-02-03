using ActualChat.Rtc;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// RPC service providing multiplexed real-time audio streams.
/// </summary>
public class RtcHub(IServiceProvider services) : IRtcHub
{
    private readonly Lock _lock = new ();
    private readonly ConcurrentDictionary<(Session, ChatId), RtcStreamMuxer> _muxers = new();

    private IServiceProvider Services { get; } = services;
    private ILogger Log => field ??= Services.LogFor<RtcHub>();

    public Task<RpcStream<RtcItem>> GetStream(
        Session session,
        ChatId chatId,
        RtcStreamingSettings settings,
        CancellationToken cancellationToken)
    {
        RtcStreamMuxer muxer;
        var key = (session, chatId);
        lock (_lock) { // TODO(AY): Make it more efficient later?
            if (_muxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync(); // No need to await for this here

            muxer = new RtcStreamMuxer(Services, session, chatId, settings);
            _muxers[key] = muxer;
        }

        var outputStream = ToAsyncEnumerable(muxer.Output, key, cancellationToken);
        var rpcStream = new RpcStream<RtcItem>(outputStream) { IsReconnectable = false };
        return Task.FromResult(rpcStream);
    }

    public Task ChangeSettings(
        Session session,
        ChatId chatId,
        RtcStreamingSettings settings,
        CancellationToken cancellationToken)
    {
        if (_muxers.TryGetValue((session, chatId), out var muxer))
            muxer.UpdateConfig(settings);

        return Task.CompletedTask;
    }

    // Private methods

    private async IAsyncEnumerable<RtcItem> ToAsyncEnumerable(
        ChannelReader<RtcItem> reader,
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
