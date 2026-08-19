using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Demultiplexes a single live stream into individual audio streams.
/// Raises events when streams start and end.
/// </summary>
public sealed class AudioStreamDemuxer(
    IAsyncEnumerable<MuxedAudioStreamItem> input,
    ILogger? log,
    CancellationTokenSource? stopTokenSource = null)
    : WorkerBase(stopTokenSource)
{
    private readonly ConcurrentDictionary<int, StreamEntry> _streams = new();

    private static bool DebugMode => Constants.DebugMode.LiveStreaming;

    private IAsyncEnumerable<MuxedAudioStreamItem> Input { get; } = input;
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
                    var startEntry = _streams.GetValueOrDefault(start.StreamIndex);
                    if (startEntry is not null) {
                        Log?.LogWarning("StreamStart N{StreamIndex}: duplicate!", start.StreamIndex);
                        continue;
                    }
                    DebugLog?.LogDebug("StreamStart N{StreamIndex}: stream #{StreamId}",
                        start.StreamIndex, start.StreamInfo.StreamId);
                    startEntry = new StreamEntry(
                        Channel.CreateUnbounded<AudioFrame>(ChannelExt.UnboundedPipeOptions));
                    _streams[start.StreamIndex] = startEntry;

                    // Note: We don't use StopToken here because the audio frames should remain
                    // readable until the channel is naturally completed (when StreamEnd is received).
                    // Using StopToken would cancel the enumeration when the demuxer stops.
                    var audioFrames = ToAsyncEnumerable(startEntry, CancellationToken.None);
                    StreamStarted?.Invoke(start.StreamInfo, start.PlaysAt, audioFrames);
                    break;
                case MuxedAudioFrame frame:
                    var frameEntry = _streams.GetValueOrDefault(frame.StreamIndex);
                    if (frameEntry is null)
                        continue;
                    if (frame.Offset < TimeSpan.Zero)
                        continue;

                    var audioFrame = new AudioFrame {
                        Data = frame.Data,
                        Offset = frame.Offset,
                        Duration = Constants.Audio.OpusFrameDuration,
                    };
                    if (!frameEntry.Channel.Writer.TryWrite(audioFrame))
                        Log?.LogWarning("Failed to write frame for stream {StreamIndex}", frame.StreamIndex);
                    frameEntry.OnFrameWritten();
                    continue;
                case MuxedAudioStreamEnd end:
                    DebugLog?.LogDebug("StreamEnd #{StreamIndex}", end.StreamIndex);
                    var endEntry = _streams.GetValueOrDefault(end.StreamIndex);
                    if (endEntry is null)
                        continue;

                    if (_streams.TryRemove(end.StreamIndex, endEntry)) {
                        Report(end.StreamIndex, endEntry);
                        endEntry.Channel.Writer.TryComplete();
                    }
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

    // Private methods

    // DebugMode is off in production, so this is the one per-track line that survives there.
    // Peak backlog is how we find out whether a listener ever falls far enough behind to need
    // catching up - the receiver itself never acts on it.
    private void Report(int streamIndex, StreamEntry entry)
        => Log?.LogInformation(
            "Stream N{StreamIndex} done: {FrameCount} frames, peak backlog {PeakBacklogMs}ms",
            streamIndex,
            entry.WrittenFrameCount,
            entry.PeakQueuedFrameCount * Constants.Audio.FrameDurationMs);

    private void FlushAllStreams()
    {
        foreach (var (index, entry) in _streams) {
            Report(index, entry);
            entry.Channel.Writer.TryComplete();
        }
        _streams.Clear();
    }

    private static async IAsyncEnumerable<AudioFrame> ToAsyncEnumerable(
        StreamEntry entry,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = entry.Channel.Reader;
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            entry.OnFrameRead();
            yield return item;
        }
    }

    // Nested types

    private sealed record StreamEntry(Channel<AudioFrame> Channel)
    {
        // Written by the demuxer loop, decremented by the player's enumerator - the count is the
        // only shared state, and it exists to be reported, never to gate delivery.
        private int _queuedFrameCount;

        public int WrittenFrameCount { get; private set; }
        public int PeakQueuedFrameCount { get; private set; }

        public void OnFrameWritten()
        {
            WrittenFrameCount++;
            var queued = Interlocked.Increment(ref _queuedFrameCount);
            if (queued > PeakQueuedFrameCount)
                PeakQueuedFrameCount = queued;
        }

        public void OnFrameRead()
            => Interlocked.Decrement(ref _queuedFrameCount);
    }
}
