using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using ActualChat.Media;

namespace ActualChat.Audio;

public class MediaFrameExtractor<TMediaFrame> where TMediaFrame : MediaFrame, new()
{
    private readonly MomentClockSet _clockSet;

    public MediaFrameExtractor(MomentClockSet clockSet)
    {
        _clockSet = clockSet;
    }

    public ChannelReader<TMediaFrame> ExtractMediaFrames(
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        var resultChannel = Channel.CreateUnbounded<TMediaFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true
        });

        _ = Task.Run(()
                => TransformAudioDataToFrames(resultChannel.Writer, audioData, cancellationToken),
            cancellationToken);

        return resultChannel.Reader;
    }

    private async Task TransformAudioDataToFrames(
        ChannelWriter<TMediaFrame> writer,
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var frameIndex = 0;
        var blockOffsetMs = 0;
        var clusterOffsetMs = 0;
        var lastState = new WebMReader.State();
        using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        try {
            while (await audioData.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (audioData.TryRead(out var blobPart)) {
                var (index, data) = blobPart;
                var remainingLength = lastState.Remaining;
                var buffer = bufferLease.Memory;

                buffer.Slice(lastState.Position,remainingLength)
                    .CopyTo(buffer[..remainingLength]);
                data.CopyTo(buffer[lastState.Remaining..]);
                var dataLength = lastState.Remaining + data.Length;

                var state = BuildAudioFrames(
                    lastState.IsEmpty
                        ? new WebMReader(bufferLease.Memory.Span[..dataLength])
                        : WebMReader.FromState(lastState).WithNewSource(bufferLease.Memory.Span[..dataLength]),
                    ref clusterOffsetMs,
                    ref blockOffsetMs,
                    ref frameIndex,
                    writer);
                lastState = state;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
        }
    }


    private WebMReader.State BuildAudioFrames(
        WebMReader webMReader,
        ref int clusterOffsetMs,
        ref int blockOffsetMs,
        ref int index,
        ChannelWriter<TMediaFrame> writer)
    {
        var prevPosition = 0;
        var beginBlocksAt = 0;
        var currentBlockOffsetMs = 0;
        while (webMReader.Read()) {
            var state = webMReader.GetState();
            switch (webMReader.ReadResultKind) {
                case WebMReadResultKind.None:
                    throw new InvalidOperationException();
                case WebMReadResultKind.Ebml:
                    break;
                case WebMReadResultKind.Segment:
                    var mediaFrame = new TMediaFrame {
                        Index = index++,
                        Offset = TimeSpan.Zero,
                        Timestamp = _clockSet.CpuClock.UtcNow,
                        Data = webMReader.Span[..state.Position].ToArray(),
                        FrameKind = MediaFrameKind.Header,
                    };
                    if (!writer.TryWrite(mediaFrame))
                        throw new InvalidOperationException("Unable to write MediaFrame");
                    break;
                case WebMReadResultKind.CompleteCluster:
                    mediaFrame = new TMediaFrame {
                        Index = index++,
                        Offset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * (clusterOffsetMs + blockOffsetMs)),
                        Timestamp = _clockSet.CpuClock.UtcNow,
                        Data = webMReader.Span[beginBlocksAt..state.Position].ToArray(),
                        FrameKind = MediaFrameKind.Blocks,
                    };
                    if (!writer.TryWrite(mediaFrame))
                        throw new InvalidOperationException("Unable to write MediaFrame");
                    break;
                case WebMReadResultKind.BeginCluster:
                    var cluster = (Cluster)webMReader.ReadResult;
                    mediaFrame = new TMediaFrame {
                        Index = index++,
                        Offset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * (long)cluster.Timestamp),
                        Timestamp = _clockSet.CpuClock.UtcNow,
                        Data = webMReader.Span[prevPosition..state.Position].ToArray(),
                        FrameKind = MediaFrameKind.Cluster,
                    };
                    beginBlocksAt = state.Position;
                    clusterOffsetMs = (int)cluster.Timestamp;
                    if (!writer.TryWrite(mediaFrame))
                        throw new InvalidOperationException("Unable to write MediaFrame");
                    break;
                case WebMReadResultKind.Block:
                    var block = (Block)webMReader.ReadResult;
                    currentBlockOffsetMs = Math.Max(currentBlockOffsetMs, block.TimeCode);
                    break;
                case WebMReadResultKind.BlockGroup:
                default:
                    throw new NotSupportedException("Unsupported EbmlEntryType.");
            }
            prevPosition = state.Position;
        }

        if (currentBlockOffsetMs > 0) {
            var mediaFrame = new TMediaFrame {
                Index = index++,
                Offset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * (clusterOffsetMs + blockOffsetMs)),
                Timestamp = _clockSet.CpuClock.UtcNow,
                Data = webMReader.Span[beginBlocksAt..prevPosition].ToArray(),
                FrameKind = MediaFrameKind.Blocks,
            };
            if (!writer.TryWrite(mediaFrame))
                throw new InvalidOperationException("Unable to write MediaFrame");

            blockOffsetMs = currentBlockOffsetMs;
        }

        return webMReader.GetState();
    }
}
