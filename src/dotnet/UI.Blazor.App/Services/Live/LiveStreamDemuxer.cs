using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.App.Services.Live;

/// <summary>
/// Demultiplexes a single live stream into individual audio streams.
/// Raises events when streams start and end.
/// </summary>
public sealed class LiveStreamDemuxer(
    RpcStream<LiveStreamItem> input,
    ILogger? log,
    CancellationTokenSource? stopTokenSource = null)
    : WorkerBase(stopTokenSource)
{
    private static bool DebugMode => Constants.DebugMode.LiveStreaming;
    private readonly ConcurrentDictionary<int, Channel<byte[]>> _streams = new();

    private RpcStream<LiveStreamItem> Input { get; } = input;
    private ILogger? Log { get; } = log;
    private ILogger? DebugLog { get; } = DebugMode ? log : null;

    public event Action<LiveStreamInfo, IAsyncEnumerable<byte[]>>? StreamStarted;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var itemCount = 0;
        try {
            await foreach (var item in Input.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                itemCount++;
                var channel = _streams.GetValueOrDefault(item.StreamIndex);
                switch (item) {
                case LiveStreamStart start:
                    if (channel is not null) {
                        Log?.LogWarning("StreamStart #{StreamIndex}: duplicate!", start.StreamIndex);
                        continue;
                    }
                    DebugLog?.LogDebug("StreamStart #{StreamIndex}, StreamId={StreamId}", start.StreamIndex, start.StreamInfo.StreamId);
                    channel = Channel.CreateUnbounded<byte[]>(ChannelExt.UnboundedPipeOptions);
                    _streams[start.StreamIndex] = channel;

                    // Note: We don't use StopToken here because the audio frames should remain
                    // readable until the channel is naturally completed (when StreamEnd is received).
                    // Using StopToken would cancel the enumeration when the demuxer stops.
                    var audioFrames = ToAsyncEnumerable(channel.Reader, CancellationToken.None);
                    StreamStarted?.Invoke(start.StreamInfo, audioFrames);
                    break;
                case LiveAudioFrame frame:
                    if (channel is null)
                        continue;
                    if (!channel.Writer.TryWrite(frame.Data))
                        Log?.LogWarning("Failed to write frame for stream {StreamIndex}", frame.StreamIndex);
                    continue;
                case LiveStreamEnd end:
                    DebugLog?.LogDebug("StreamEnd #{StreamIndex}", end.StreamIndex);
                    if (channel is null)
                        continue;
                    if (_streams.TryRemove(end.StreamIndex, channel))
                        channel.Writer.TryComplete();
                    break;
                }
            }
            DebugLog?.LogInformation("Stream ended normally after {ItemCount} items", itemCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            DebugLog?.LogDebug("Cancelled after {ItemCount} items", itemCount);
        }
        catch (RpcReconnectFailedException e) {
            Log?.LogError(e, "Reconnect failed after {ItemCount} items", itemCount);
        }
        catch (Exception e) {
            Log?.LogError(e, "Error processing live stream after {ItemCount} items", itemCount);
        }
        finally {
            // Clean up all remaining streams; we don't propagate the error here
            foreach (var (_, channel) in _streams)
                channel.Writer.TryComplete();
            _streams.Clear();
        }
    }

    private static async IAsyncEnumerable<byte[]> ToAsyncEnumerable(
        ChannelReader<byte[]> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}
