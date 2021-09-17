using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;

namespace ActualChat.Audio.Orchestration
{
    // TODO(AY): Quite complicated thing... Why it doesn't return ~ a sequence of AudioRecordSegment
    // (i.e. boundaries only) & let another service to be streaming ANY audio record segments?
    public class AudioActivityExtractor
    {
        public IAsyncEnumerable<AudioRecordSegmentAccessor> GetSegmentsWithAudioActivity(
            AudioRecord audioRecord,
            ChannelReader<BlobPart> audioReader)
            => GetSegmentsWithAudioActivity(audioRecord, audioReader, default);

        private async IAsyncEnumerable<AudioRecordSegmentAccessor> GetSegmentsWithAudioActivity(
            AudioRecord audioRecord,
            ChannelReader<BlobPart> audioReader,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var segmentChannel = Channel.CreateUnbounded<AudioRecordSegmentAccessor>(
                new UnboundedChannelOptions {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            // TODO(AK): add exception handling
            var extractionTask = Task.Run(
                () => ExtractSegments(audioRecord, audioReader, segmentChannel, cancellationToken), default);

            await foreach (var segment in segmentChannel.Reader.ReadAllAsync(cancellationToken))
                yield return segment;

            await extractionTask;
        }

        private async Task ExtractSegments(
            AudioRecord audioRecord,
            ChannelReader<BlobPart> audioReader,
            ChannelWriter<AudioRecordSegmentAccessor> segmentWriter,
            CancellationToken cancellationToken)
        {
            var segmentIndex = 0;
            var metaData = new List<AudioMetadataEntry>();
            var documentBuilder = new WebMDocumentBuilder();
            var audioChannel = Channel.CreateUnbounded<BlobPart>(
                new UnboundedChannelOptions {
                    SingleReader = false,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
            var segment = new AudioRecordSegmentAccessor(
                segmentIndex,
                audioRecord,
                documentBuilder,
                metaData,
                0d,
                audioChannel.Reader);
            await segmentWriter.WriteAsync(segment, cancellationToken);

            WebMReader.State lastState = new WebMReader.State();
            using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);

            await foreach (var audioMessage in audioReader.ReadAllAsync(cancellationToken)) {
                var (index, chunk) = audioMessage;

                metaData.Add(new AudioMetadataEntry(index, 0, 0));
                var remainingLength = lastState.Remaining;
                var buffer = bufferLease.Memory;

                buffer.Slice(lastState.Position,remainingLength).CopyTo(buffer[..remainingLength]);
                chunk.CopyTo(buffer[lastState.Remaining..]);

                var dataLength = lastState.Remaining + chunk.Length;

                // TODO(AK): get actual duration\offset from Clusters\SimpleBlocks and fill metaData
                var state = BuildWebMDocument(
                    lastState.IsEmpty
                        ? new WebMReader(bufferLease.Memory.Span[..dataLength])
                        : WebMReader.FromState(lastState).WithNewSource(bufferLease.Memory.Span[..dataLength]),
                    documentBuilder);
                lastState = state;

                // We don't use WebMWriter yet because we can't read blocks directly yet. So we don't split actually
                await audioChannel.Writer.WriteAsync(audioMessage, cancellationToken);

                // TODO(AK): Implement VAD and perform actual audio split
            }

            if (lastState.Container is Cluster cluster) {
                cluster.Complete();
                documentBuilder.AddCluster(cluster);
            }

            segmentWriter.Complete();
            audioChannel.Writer.Complete();
            await segment.CompleteBuffering();
        }

        // TODO(AK): we should read blocks there
        private WebMReader.State BuildWebMDocument(WebMReader webMReader, WebMDocumentBuilder builder)
        {
            while (webMReader.Read())
                switch (webMReader.EbmlEntryType) {
                    case EbmlEntryType.None:
                        throw new InvalidOperationException();
                    case EbmlEntryType.Ebml:
                        // TODO: add support of EBML Stream where multiple headers and segments can appear
                        builder.SetHeader((EBML)webMReader.Entry);
                        break;
                    case EbmlEntryType.Segment:
                        webMReader.Entry.Complete();
                        builder.SetSegment((Segment)webMReader.Entry);
                        break;
                    case EbmlEntryType.Cluster:
                        webMReader.Entry.Complete();
                        builder.AddCluster((Cluster)webMReader.Entry);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

            return webMReader.GetState();
        }
    }
}
