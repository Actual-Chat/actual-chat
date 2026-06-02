using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Demultiplexes a single live stream into individual audio streams.
/// Raises events when streams start and end.
/// </summary>
public sealed class AudioStreamDemuxer(
    IAsyncEnumerable<MuxedStreamItem> input,
    ILogger? log,
    CancellationTokenSource? stopTokenSource = null)
    : WorkerBase(stopTokenSource)
{
    private static bool DebugMode => Constants.DebugMode.LiveStreaming;
    private readonly ConcurrentDictionary<int, Channel<AudioFrame>> _streams = new();

    private IAsyncEnumerable<MuxedStreamItem> Input { get; } = input;
    private ILogger? Log { get; } = log;
    private ILogger? DebugLog { get; } = DebugMode ? log : null;

    public event Action<LiveAudioStreamInfo, TimeSpan, IAsyncEnumerable<AudioFrame>>? StreamStarted;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var itemCount = 0;
        try {
            await foreach (var item in Input.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                itemCount++;
                switch (item) {
                case MuxedAudioStreamReset:
                    DebugLog?.LogDebug("StreamReset: flushing {Count} in-flight streams", _streams.Count);
                    FlushAllStreams();
                    continue;
                case MuxedAudioStreamStart start:
                    var startChannel = _streams.GetValueOrDefault(start.StreamIndex);
                    if (startChannel is not null) {
                        Log?.LogWarning("StreamStart N{StreamIndex}: duplicate!", start.StreamIndex);
                        continue;
                    }
                    DebugLog?.LogDebug("StreamStart N{StreamIndex}: stream #{StreamId}", start.StreamIndex, start.StreamInfo.StreamId);
                    startChannel = Channel.CreateUnbounded<AudioFrame>(ChannelExt.UnboundedPipeOptions);
                    _streams[start.StreamIndex] = startChannel;

                    // Note: We don't use StopToken here because the audio frames should remain
                    // readable until the channel is naturally completed (when StreamEnd is received).
                    // Using StopToken would cancel the enumeration when the demuxer stops.
                    var audioFrames = ToAsyncEnumerable(startChannel.Reader, CancellationToken.None);
                    StreamStarted?.Invoke(start.StreamInfo, start.PlaysAt, audioFrames);
                    break;
                case MuxedAudioFrame frame:
                    var frameChannel = _streams.GetValueOrDefault(frame.StreamIndex);
                    if (frameChannel is null)
                        continue;
                    if (frame.Offset < TimeSpan.Zero)
                        continue;

                    var audioFrame = new AudioFrame {
                        Data = frame.Data,
                        Offset = frame.Offset,
                        Duration = Constants.Audio.OpusFrameDuration,
                    };
                    if (!frameChannel.Writer.TryWrite(audioFrame))
                        Log?.LogWarning("Failed to write frame for stream {StreamIndex}", frame.StreamIndex);
                    continue;
                case MuxedAudioStreamEnd end:
                    DebugLog?.LogDebug("StreamEnd #{StreamIndex}", end.StreamIndex);
                    var endChannel = _streams.GetValueOrDefault(end.StreamIndex);
                    if (endChannel is null)
                        continue;
                    if (_streams.TryRemove(end.StreamIndex, endChannel))
                        endChannel.Writer.TryComplete();
                    break;
                }
            }
            DebugLog?.LogInformation("Stream ended normally after {ItemCount} items", itemCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            DebugLog?.LogDebug("Cancelled after {ItemCount} items", itemCount);
        }
        catch (Exception e) {
            Log?.LogError(e, "Error processing live stream after {ItemCount} items", itemCount);
        }
        finally {
            // Clean up all remaining streams; we don't propagate the error here
            FlushAllStreams();
        }
    }

    private void FlushAllStreams()
    {
        foreach (var (_, channel) in _streams)
            channel.Writer.TryComplete();
        _streams.Clear();
    }

    private static async IAsyncEnumerable<AudioFrame> ToAsyncEnumerable(
        ChannelReader<AudioFrame> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}
