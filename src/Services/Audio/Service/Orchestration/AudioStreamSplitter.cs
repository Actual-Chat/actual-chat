using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Streaming;

namespace ActualChat.Audio.Orchestration
{
    public readonly struct AudioStreamSplitter
    {
        public async IAsyncEnumerable<AudioStreamEntry> SplitBySilencePeriods(
            AudioRecording audioRecording,
            ChannelReader<BlobMessage> audioReader,
            [EnumeratorCancellation]CancellationToken cancellationToken)
        {
            var entryChannel = Channel.CreateUnbounded<AudioStreamEntry>(
                new UnboundedChannelOptions {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            // TODO(AK): add exception handling
            var buildTask = BuildStreamEntries(
                audioRecording,
                audioReader,
                entryChannel,
                cancellationToken);
            
            await foreach (var entry in entryChannel.Reader.ReadAllAsync(cancellationToken)) 
                yield return entry;

            await buildTask;
        }

        private async Task BuildStreamEntries(
            AudioRecording audioRecording,
            ChannelReader<BlobMessage> audioReader,
            ChannelWriter<AudioStreamEntry> entryWriter,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            
            var entryIndex = 0;
            var metaData = new List<AudioMetaDataEntry>();
            var documentBuilder = new WebMDocumentBuilder();
            var audioChannel = Channel.CreateUnbounded<BlobMessage>(
                new UnboundedChannelOptions {
                    SingleReader = false,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
            var audioStreamEntry = new AudioStreamEntry(
                entryIndex,
                audioRecording,
                documentBuilder,
                metaData,
                0d,
                audioChannel.Reader);
            await entryWriter.WriteAsync(audioStreamEntry, cancellationToken);
            
            WebMReader.State lastState = new WebMReader.State();
            using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);

            await foreach (var audioMessage in audioReader.ReadAllAsync(cancellationToken)) {
                var (index, chunk) = audioMessage;
                
                metaData.Add(new AudioMetaDataEntry(index, 0, 0));
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
                
                // we don't use WebMWriter yet because we can't read blocks directly yet. So we don't split actually
                await audioChannel.Writer.WriteAsync(audioMessage, cancellationToken);
                
                // TODO(AK): Implement VAD and perform actual audio split
            }

            if (lastState.Container is Cluster cluster) {
                cluster.Complete();
                documentBuilder.AddCluster(cluster);
            }

            entryWriter.Complete();
            audioChannel.Writer.Complete();
            await audioStreamEntry.CompleteBuffering();
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