using ActualChat.Rtc;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.App.Services.Rtc;

/// <summary>
/// Demultiplexes a single RTC stream into individual audio streams.
/// Raises events when streams start and end.
/// </summary>
public sealed class RtcStreamDemuxer(
    RpcStream<RtcItem> input,
    ILogger log,
    CancellationTokenSource? stopTokenSource = null)
    : WorkerBase(stopTokenSource)
{
    private readonly ConcurrentDictionary<int, Channel<byte[]>> _streams = new();

    private RpcStream<RtcItem> Input { get; } = input;
    private ILogger Log { get; } = log;

    public event Action<RtcStreamStartedArgs>? StreamStarted;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in Input.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                switch (item) {
                case RtcStreamStart start:
                    HandleStreamStart(start);
                    break;
                case RtcAudioFrame frame:
                    HandleAudioFrame(frame);
                    break;
                case RtcStreamEnd end:
                    HandleStreamEnd(end);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected
        }
        catch (RpcReconnectFailedException e) {
            Log.LogError(e, "Reconnect failed");
        }
        catch (Exception e) {
            Log.LogError(e, "Error processing RTC stream");
        }
        finally {
            // Clean up all remaining streams; we don't propagate the error here
            foreach (var (_, channel) in _streams)
                channel.Writer.TryComplete();
            _streams.Clear();
        }
    }

    private void HandleStreamStart(RtcStreamStart start)
    {
        var channel = Channel.CreateUnbounded<byte[]>(ChannelExt.SingleReaderWriterUnboundedChannelOptions);
        if (!_streams.TryAdd(start.StreamIndex, channel)) {
            Log.LogWarning("Duplicate stream start for index {StreamIndex}", start.StreamIndex);
            return;
        }

        var audioFrames = ToAsyncEnumerable(channel.Reader, StopToken);
        var args = new RtcStreamStartedArgs(start, audioFrames);
        StreamStarted?.Invoke(args);
    }

    private void HandleAudioFrame(RtcAudioFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamIndex, out var channel)) {
            // Stream not found - might have ended already
            return;
        }

        if (!channel.Writer.TryWrite(frame.Data))
            Log.LogWarning("Failed to write frame for stream {StreamIndex}", frame.StreamIndex);
    }

    private void HandleStreamEnd(RtcStreamEnd end)
    {
        if (!_streams.TryRemove(end.StreamIndex, out var channel))
            return;

        channel.Writer.TryComplete();
    }

    private static async IAsyncEnumerable<byte[]> ToAsyncEnumerable(
        ChannelReader<byte[]> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}
