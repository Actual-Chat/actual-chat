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
    private static readonly int MaxTrackBacklogFrames =
        (int)(Constants.Audio.MaxTrackBacklog / Constants.Audio.OpusFrameDuration);

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
                    DropBacklogged(start.StreamIndex);
                    // FanOut, not Pipe: the backlog drop reads this channel from the demuxer
                    // while the player may be reading it too, and SingleReader also makes
                    // Reader.Count - the backlog metric - throw.
                    startEntry = new StreamEntry(
                        Channel.CreateUnbounded<AudioFrame>(ChannelExt.UnboundedFanOutOptions));
                    _streams[start.StreamIndex] = startEntry;

                    // Note: We don't use StopToken here because the audio frames should remain
                    // readable until the channel is naturally completed (when StreamEnd is received).
                    // Using StopToken would cancel the enumeration when the demuxer stops.
                    var audioFrames = ToAsyncEnumerable(startEntry.Channel.Reader, CancellationToken.None);
                    StreamStarted?.Invoke(start.StreamInfo, start.PlaysAt, audioFrames);
                    break;
                case MuxedAudioFrame frame:
                    var frameEntry = _streams.GetValueOrDefault(frame.StreamIndex);
                    if (frameEntry is null || frameEntry.IsEnded)
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

                    // The entry outlives StreamEnd until drained - an ended track with seconds
                    // still queued is exactly the stale one worth dropping.
                    endEntry.IsEnded = true;
                    endEntry.Channel.Writer.TryComplete();
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

    private void DropBacklogged(int startingStreamIndex)
    {
        foreach (var (index, entry) in _streams) {
            if (index == startingStreamIndex)
                continue;

            var backlogFrames = entry.Channel.Reader.Count;
            entry.OnBacklogSampled(backlogFrames);
            if (entry.IsEnded && backlogFrames == 0) {
                Report(index, entry, false);
                _streams.TryRemove(index, entry);
                continue;
            }

            if (backlogFrames <= MaxTrackBacklogFrames)
                continue;

            // Information, not debug: DebugMode.LiveStreaming is false in production, and a
            // dropped track is the one thing here a listener can actually hear.
            Log?.LogInformation(
                "Dropping stream N{StreamIndex}: {BacklogMs}ms queued exceeds {LimitMs}ms",
                index,
                backlogFrames * Constants.Audio.FrameDurationMs,
                Constants.Audio.MaxTrackBacklog.TotalMilliseconds);
            while (entry.Channel.Reader.TryRead(out _)) { }
            entry.IsEnded = true;
            entry.Channel.Writer.TryComplete();
            Report(index, entry, true);
            _streams.TryRemove(index, entry);
        }
    }

    // Peak backlog is what sizes MaxTrackBacklog, and DebugMode is off in production - so this
    // is the one per-track line that has to survive there.
    private void Report(int streamIndex, StreamEntry entry, bool isDropped)
        => Log?.LogInformation(
            "Stream N{StreamIndex} done: {FrameCount} frames, peak backlog {PeakBacklogMs}ms, dropped={IsDropped}",
            streamIndex,
            entry.WrittenFrameCount,
            entry.PeakBacklogFrames * Constants.Audio.FrameDurationMs,
            isDropped);

    private void FlushAllStreams()
    {
        foreach (var (index, entry) in _streams) {
            entry.OnBacklogSampled(entry.Channel.Reader.Count);
            Report(index, entry, false);
            entry.Channel.Writer.TryComplete();
        }
        _streams.Clear();
    }

    private static async IAsyncEnumerable<AudioFrame> ToAsyncEnumerable(
        ChannelReader<AudioFrame> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    // Nested types

    private sealed record StreamEntry(Channel<AudioFrame> Channel)
    {
        public bool IsEnded { get; set; }
        public int WrittenFrameCount { get; private set; }
        public int PeakBacklogFrames { get; private set; }

        public void OnFrameWritten()
            => WrittenFrameCount++;

        public void OnBacklogSampled(int backlogFrames)
        {
            if (backlogFrames > PeakBacklogFrames)
                PeakBacklogFrames = backlogFrames;
        }
    }
}
