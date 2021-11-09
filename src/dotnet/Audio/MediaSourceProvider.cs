using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using ActualChat.Media;

namespace ActualChat.Audio;

public abstract class MediaSourceProvider<TMediaSource, TMediaFormat, TMediaFrame>
    where TMediaSource : MediaSource<TMediaFormat, TMediaFrame>
    where TMediaFormat : MediaFormat
    where TMediaFrame : MediaFrame, new()
{
    public ValueTask<TMediaSource> CreateMediaSource(
        IAsyncEnumerable<BlobPart> blobParts,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var frames = Channel.CreateUnbounded<TMediaFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        var formatTaskSource = TaskSource.New<TMediaFormat>(true);
        var durationTaskSource = TaskSource.New<TimeSpan>(true);
        _ = Task.Run(
            () => TransformBlobPartsToFrames(
                frames.Writer,
                formatTaskSource,
                durationTaskSource,
                blobParts,
                skipTo,
                cancellationToken),
            cancellationToken);

        return CreateMediaSource(formatTaskSource.Task,
            durationTaskSource.Task,
            frames.Reader.Memoize(cancellationToken));
    }

    protected abstract ValueTask<TMediaSource> CreateMediaSource(
        Task<TMediaFormat> formatTask,
        Task<TimeSpan> durationTask,
        AsyncMemoizer<TMediaFrame> frameMemoizer);

    protected abstract TMediaFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader);

    private async Task TransformBlobPartsToFrames(
        ChannelWriter<TMediaFrame> target,
        TaskSource<TMediaFormat> formatTaskSource,
        TaskSource<TimeSpan> durationTaskSource,
        IAsyncEnumerable<BlobPart> blobParts,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var blockOffsetMs = 0;
        var clusterOffsetMs = 0;
        var lastState = new WebMReader.State();
        using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        try {
            await foreach (var blobPart in blobParts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var (_, data) = blobPart;
                var remainingLength = lastState.Remaining;
                var buffer = bufferLease.Memory;

                buffer.Slice(lastState.Position, remainingLength)
                    .CopyTo(buffer[..remainingLength]);
                data.CopyTo(buffer[lastState.Remaining..]);
                var dataLength = lastState.Remaining + data.Length;

                var state = BuildAudioFrames(
                    lastState.IsEmpty
                        ? new WebMReader(bufferLease.Memory.Span[..dataLength])
                        : WebMReader.FromState(lastState).WithNewSource(bufferLease.Memory.Span[..dataLength]),
                    skipTo,
                    formatTaskSource,
                    ref clusterOffsetMs,
                    ref blockOffsetMs,
                    target);
                lastState = state;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            error = e;
        }
        finally {
            target.TryComplete(error);
            if (error != null) {
                formatTaskSource.TrySetException(error);
                durationTaskSource.TrySetException(error);
            }
            else {
                if (!formatTaskSource.Task.IsCompleted)
                    formatTaskSource.TrySetCanceled(cancellationToken);
                var durationWithoutLastBlock
                    = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * (clusterOffsetMs + blockOffsetMs));
                durationTaskSource.TrySetResult(durationWithoutLastBlock);
            }
        }
    }

    private WebMReader.State BuildAudioFrames(
        WebMReader webMReader,
        TimeSpan skipTo,
        TaskSource<TMediaFormat> formatTaskSource,
        ref int clusterOffsetMs,
        ref int blockOffsetMs,
        ChannelWriter<TMediaFrame> writer)
    {
        var prevPosition = 0;
        var framesStart = 0;
        var currentBlockOffsetMs = 0;
        var skipToMs = (int)skipTo.TotalMilliseconds;
        EBML? ebml = null;
        Segment? segment = null;
        TMediaFrame? firstFrame = null;
        using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        while (webMReader.Read()) {
            var state = webMReader.GetState();
            switch (webMReader.ReadResultKind) {
                case WebMReadResultKind.None:
                    throw new InvalidOperationException();
                case WebMReadResultKind.Ebml:
                    ebml = (EBML)webMReader.ReadResult;
                    break;
                case WebMReadResultKind.Segment:
                    segment = (Segment)webMReader.ReadResult;
                    break;
                case WebMReadResultKind.CompleteCluster:
                    break;
                case WebMReadResultKind.BeginCluster:
                    if (!formatTaskSource.Task.IsCompleted) {
                        var format = CreateMediaFormat(ebml!, segment!, webMReader.Span[..state.Position]);
                        formatTaskSource.SetResult(format);
                        framesStart = state.Position;
                    }
                    var cluster = (Cluster)webMReader.ReadResult;
                    clusterOffsetMs = (int)cluster.Timestamp;
                    currentBlockOffsetMs = 0;
                    break;
                case WebMReadResultKind.Block:
                    var block = (Block)webMReader.ReadResult;
                    currentBlockOffsetMs = Math.Max(currentBlockOffsetMs, block.TimeCode);
                    if (block is SimpleBlock { IsKeyFrame: true } simpleBlock) {
                        TMediaFrame mediaFrame;
                        var originalFrameOffset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond
                            * (clusterOffsetMs + currentBlockOffsetMs));

                        if (skipToMs > 0) {
                            var clusterSkipToMs = skipToMs - clusterOffsetMs;
                            TimeSpan frameOffset;
                            if (simpleBlock.TimeCode >= clusterSkipToMs) {
                                simpleBlock.TimeCode -= (short)clusterSkipToMs;
                                frameOffset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond
                                    * (clusterOffsetMs + simpleBlock.TimeCode));
                            }
                            else {
                                simpleBlock.TimeCode = 0;
                                frameOffset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * clusterOffsetMs);
                            }
                            var webMWriter = new WebMWriter(bufferLease.Memory.Span);
                            webMWriter.Write(simpleBlock);

                            mediaFrame = new TMediaFrame {
                                Offset = frameOffset,
                                Data = webMWriter.Written.ToArray(),
                            };
                        }
                        else
                            mediaFrame = new TMediaFrame {
                                Offset = originalFrameOffset,
                                Data = webMReader.Span[framesStart..state.Position].ToArray(),
                            };

                        if (skipTo <= originalFrameOffset) {
                            if (firstFrame != null) {
                                if (!writer.TryWrite(firstFrame))
                                    throw new InvalidOperationException("Unable to write MediaFrame.");

                                firstFrame = null;
                            }
                            if (!writer.TryWrite(mediaFrame))
                                throw new InvalidOperationException("Unable to write MediaFrame.");
                        }
                        else
                            firstFrame = mediaFrame;

                        framesStart = state.Position;
                        blockOffsetMs = currentBlockOffsetMs;
                    }
                    break;
                case WebMReadResultKind.BlockGroup:
                default:
                    throw new NotSupportedException("Unsupported EbmlEntryType.");
            }
            prevPosition = state.Position;
        }

        if (framesStart != prevPosition)
            throw new InvalidOperationException("Unexpected WebM structure.");

        blockOffsetMs = currentBlockOffsetMs;

        return webMReader.GetState();
    }
}
