using System.Buffers;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;

namespace ActualChat.Audio.Processing;

// TODO(AY): Quite complicated thing... Why it doesn't return ~ a sequence of AudioRecordSegment
// (i.e. boundaries only) & let another service to be streaming ANY audio record segments?
public class AudioActivityExtractor
{
    public ChannelReader<AudioRecordSegment> GetSegmentsWithAudioActivity(
        AudioRecord audioRecord,
        ChannelReader<BlobPart> audioReader,
        CancellationToken cancellationToken)
    {
        var segments = Channel.CreateUnbounded<AudioRecordSegment>(
            new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });
        _ = Task.Run(() => ExtractSegments(audioRecord, audioReader, segments.Writer, cancellationToken), default);
        return segments;
    }

    private async Task ExtractSegments(
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        ChannelWriter<AudioRecordSegment> target,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var segmentIndex = 0;
        var webmBuilder = new WebMDocumentBuilder();
        var metadata = new List<AudioMetadataEntry>();
        var audioSource = Channel.CreateUnbounded<BlobPart>(
            new UnboundedChannelOptions {
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        var segment = new AudioRecordSegment(
            segmentIndex, audioRecord,
            webmBuilder, metadata, 0, audioSource);
        await target.WriteAsync(segment, cancellationToken);
        try {
            var lastState = new WebMReader.State();
            using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
            await foreach (var part in content.ReadAllAsync(cancellationToken)) {
                var (index, data) = part;

                metadata.Add(new AudioMetadataEntry(index, 0, 0));
                var remainingLength = lastState.Remaining;
                var buffer = bufferLease.Memory;

                buffer.Slice(lastState.Position, remainingLength)
                    .CopyTo(buffer[..remainingLength]);
                data.CopyTo(buffer[lastState.Remaining..]);
                var dataLength = lastState.Remaining + data.Length;

                // TODO(AK): get actual duration\offset from Clusters\SimpleBlocks and fill metaData
                var state = BuildWebMDocument(
                    lastState.IsEmpty
                        ? new WebMReader(bufferLease.Memory.Span[..dataLength])
                        : WebMReader.FromState(lastState).WithNewSource(bufferLease.Memory.Span[..dataLength]),
                    webmBuilder);
                lastState = state;

                // We don't use WebMWriter yet because we can't read blocks directly yet. So we don't split actually
                await audioSource.Writer.WriteAsync(part, cancellationToken);

                // TODO(AK): Implement VAD and perform actual audio split
            }

            if (lastState.Container is Cluster cluster) {
                cluster.Complete();
                webmBuilder.AddCluster(cluster);
            }
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            audioSource.Writer.Complete(error);
            target.Complete(error);
        }
    }

    // TODO(AK): we should read blocks there
    private WebMReader.State BuildWebMDocument(WebMReader webMReader, WebMDocumentBuilder builder)
    {
        while (webMReader.Read())
            switch (webMReader.ReadResultKind) {
            case WebMReadResultKind.None:
                throw new InvalidOperationException();
            case WebMReadResultKind.Ebml:
                // TODO: add support of EBML Stream where multiple headers and segments can appear
                builder.SetHeader((EBML)webMReader.ReadResult);
                break;
            case WebMReadResultKind.Segment:
                ((Segment)webMReader.ReadResult).Complete();
                builder.SetSegment((Segment)webMReader.ReadResult);
                break;
            case WebMReadResultKind.CompleteCluster:
                ((Cluster)webMReader.ReadResult).Complete();
                builder.AddCluster((Cluster)webMReader.ReadResult);
                break;
            case WebMReadResultKind.Block:
                break;
            case WebMReadResultKind.BeginCluster:
                break;
            default:
                throw new NotSupportedException("Unsupported EbmlEntryType.");
            }

        return webMReader.GetState();
    }
}
