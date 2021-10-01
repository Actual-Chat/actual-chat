using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;

namespace ActualChat.Audio;

public class AudioFrameExtractor
{
    private readonly MomentClockSet _clockSet;

    public AudioFrameExtractor(MomentClockSet clockSet)
    {
        _clockSet = clockSet;
    }

    public ChannelReader<AudioFrame> ExtractAudioFrames(
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        var resultChannel = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
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
        ChannelWriter<AudioFrame> writer,
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
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

                AudioFrame? frame;
                var state = BuildAudioFrame(
                    lastState.IsEmpty
                        ? new WebMReader(bufferLease.Memory.Span[..dataLength])
                        : WebMReader.FromState(lastState).WithNewSource(bufferLease.Memory.Span[..dataLength]),
                    index,
                    out frame);
                lastState = state;

                // We don't use WebMWriter yet because we can't read blocks directly yet. So we don't split actually
                // await audioSource.Writer.WriteAsync(part, cancellationToken);


            }

            if (lastState.Container is Cluster cluster) {
                cluster.Complete();
                // webmBuilder.AddCluster(cluster);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
        }
    }


    private WebMReader.State BuildAudioFrame(WebMReader webMReader, int index, out AudioFrame? audioFrame)
    {
        audioFrame = null;
        var prevPosition = 0;
        while (webMReader.Read()) {
            var state = webMReader.GetState();
            switch (webMReader.EbmlEntryType) {
                case EbmlEntryType.None:
                    throw new InvalidOperationException();
                case EbmlEntryType.Ebml:
                    break;
                case EbmlEntryType.Segment:
                    // audioFrame = new AudioFrame(
                    //     index,
                    //     AudioFrameKind.Header,
                    //     Data: webMReader.Span[..state.Position].ToArray(),
                    //     Offset: 0,
                    //     _clockSet.CpuClock.UtcNow,
                    //     BlobsStartAt: null
                    // );
                    return state;
                case EbmlEntryType.Cluster:
                    // webMReader.Entry.Complete();
                    // builder.AddCluster((Cluster)webMReader.Entry);
                    break;
                default:
                    throw new NotSupportedException("Unsupported EbmlEntryType.");
            }
            prevPosition = state.Position;
        }

        if (index == 1) {
            var cluster = (Cluster)webMReader.Entry;
            // audioFrame = new AudioFrame(
            //     index,
            //     AudioFrameKind.ClusterAndBlobs,
            //     Data: webMReader.Span[..prevPosition].ToArray(),
            //     Offset: (double)cluster.Timestamp / 1000,
            //     _clockSet.CpuClock.UtcNow,
            //     BlobsStartAt: null
            // );
        }
        else {
            var cluster = (Cluster)webMReader.Entry;
            // audioFrame = new AudioFrame(
            //     index,
            //     AudioFrameKind.Blobs,
            //     Data: webMReader.Span[..prevPosition].ToArray(),
            //     Offset: (double)cluster.Timestamp / 1000,
            //     _clockSet.CpuClock.UtcNow,
            //     BlobsStartAt: null
            // );
        }

        return webMReader.GetState();
    }
}
