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
    public ValueTask<TMediaSource> ExtractMediaSource(
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        var frameChannel = Channel.CreateUnbounded<TMediaFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        var formatTaskSource = TaskSource.New<TMediaFormat>(true);
        var durationTaskSource = TaskSource.New<TimeSpan>(true);
        _ = Task.Run(
            () => TransformAudioDataToFrames(formatTaskSource,
                durationTaskSource,
                frameChannel.Writer,
                audioData,
                cancellationToken),
            cancellationToken);

        return CreateMediaSource(formatTaskSource.Task,
            durationTaskSource.Task,
            frameChannel.Reader.Memoize(cancellationToken));
    }

    protected abstract ValueTask<TMediaSource> CreateMediaSource(
        Task<TMediaFormat> formatTask,
        Task<TimeSpan> durationTask,
        AsyncMemoizer<TMediaFrame> frameMemoizer);

    protected abstract TMediaFormat CreateMediaFormat(EBML ebml, Segment segment, ReadOnlySpan<byte> rawHeader);

    private async Task TransformAudioDataToFrames(
        TaskSource<TMediaFormat> formatTaskSource,
        TaskSource<TimeSpan> durationTaskSource,
        ChannelWriter<TMediaFrame> writer,
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var blockOffsetMs = 0;
        var clusterOffsetMs = 0;
        var lastState = new WebMReader.State();
        using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
        try {
            while (await audioData.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (audioData.TryRead(out var blobPart)) {
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
                    formatTaskSource,
                    ref clusterOffsetMs,
                    ref blockOffsetMs,
                    writer);
                lastState = state;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
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
        TaskSource<TMediaFormat> formatTaskSource,
        ref int clusterOffsetMs,
        ref int blockOffsetMs,
        ChannelWriter<TMediaFrame> writer)
    {
        var prevPosition = 0;
        var framesStart = 0;
        var currentBlockOffsetMs = 0;
        EBML? ebml = null;
        Segment? segment = null;
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
                    break;
                case WebMReadResultKind.Block:
                    var block = (Block)webMReader.ReadResult;
                    currentBlockOffsetMs = Math.Max(currentBlockOffsetMs, block.TimeCode);
                    if (block is SimpleBlock { IsKeyFrame: true }) {
                        var mediaFrame = new TMediaFrame {
                            Offset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond
                                * (clusterOffsetMs + currentBlockOffsetMs)),
                            Data = webMReader.Span[framesStart..state.Position].ToArray(),
                        };
                        if (!writer.TryWrite(mediaFrame))
                            throw new InvalidOperationException("Unable to write MediaFrame");

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

        if (framesStart != prevPosition) {
            var finalMediaFrame = new TMediaFrame {
                Offset = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * (clusterOffsetMs + blockOffsetMs)),
                Data = webMReader.Span[framesStart..prevPosition].ToArray(),
            };
            if (!writer.TryWrite(finalMediaFrame))
                throw new InvalidOperationException("Unable to write MediaFrame");
        }

        blockOffsetMs = currentBlockOffsetMs;

        return webMReader.GetState();
    }
}
