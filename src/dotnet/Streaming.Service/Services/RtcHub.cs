using ActualChat.Rtc;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// RPC service providing multiplexed real-time audio streams.
/// </summary>
public class RtcHub(IServiceProvider services) : IRtcHub
{
    private static readonly ConcurrentDictionary<(Session, ChatId), RtcStreamMuxer> Muxers = new();

    private IServiceProvider Services { get; } = services;
    private ILogger Log => field ??= Services.LogFor<RtcHub>();

    public async Task<RpcStream<RtcItem>> GetStream(
        Session session,
        ChatId chatId,
        RtcStreamConfig config,
        CancellationToken cancellationToken)
    {
        var key = (session, chatId);

        // Clean up any existing muxer for this session/chat
        if (Muxers.TryRemove(key, out var oldMuxer))
            await oldMuxer.DisposeAsync().ConfigureAwait(false);

        // Create new muxer
        var muxer = new RtcStreamMuxer(Services, session, chatId, config);
        Muxers[key] = muxer;

        // Convert to async enumerable and wrap as RpcStream
        var outputStream = ToAsyncEnumerable(muxer.Output, key, cancellationToken);
        return RpcStream.New(outputStream);
    }

    public Task UpdateConfig(
        Session session,
        ChatId chatId,
        RtcStreamConfig config,
        CancellationToken cancellationToken)
    {
        var key = (session, chatId);
        if (Muxers.TryGetValue(key, out var muxer))
            muxer.UpdateConfig(config);

        return Task.CompletedTask;
    }

    // Private methods

    private static async IAsyncEnumerable<RtcItem> ToAsyncEnumerable(
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
            if (Muxers.TryRemove(key, out var muxer))
                await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
