using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Distribution;

namespace ActualChat.Audio.Orchestration
{
    public readonly struct AudioStreamSplitter
    {
        public async IAsyncEnumerable<AudioStreamEntry> SplitBySilencePeriods(
            AudioRecording audioRecording,
            ChannelReader<AudioMessage> audioReader,
            [EnumeratorCancellation]CancellationToken cancellationToken)
        {
            var entryIndex = 0;
            var streamId = GetStreamId(audioRecording.Id, entryIndex);
            var metaData = new List<AudioMetaDataEntry>();
            var entryChannel = Channel.CreateUnbounded<AudioMessage>(
                new UnboundedChannelOptions {
                    SingleReader = false,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });
            var audioStreamEntry = new AudioStreamEntry(audioRecording, streamId, metaData, entryChannel.Reader);
            yield return audioStreamEntry;
            
            var lastOffset = audioRecording.Configuration.ClientStartOffset;
            EBML? ebml = null; 
            Segment? segment = null;
            WebMReader.State readerState = new WebMReader.State();
            var clusters = new List<Cluster>();
            var currentSegmentDuration = 0d;
            using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
            await foreach (var (index, clientEndOffset, chunk) in audioReader.ReadAllAsync(cancellationToken)) {
                var duration = clientEndOffset - lastOffset;
                metaData.Add(new AudioMetaDataEntry(index, lastOffset, duration));
                var remainingLength = readerState.Remaining;
                var buffer = bufferLease.Memory;
                
                buffer.Slice(readerState.Position,remainingLength).CopyTo(buffer[..remainingLength]);
                chunk.CopyTo(buffer[readerState.Remaining..]);

                var dataLength = readerState.Remaining + chunk.Length;
                
                var (cs, s) = ReadClusters(
                    readerState.IsEmpty 
                        ? new WebMReader(bufferLease.Memory.Span[..dataLength]) 
                        : WebMReader.FromState(readerState).WithNewSource(bufferLease.Memory.Span[..dataLength]));
                readerState = s;
                clusters.AddRange(cs);
                currentSegmentDuration += duration;
                
                // we don't use WebMWriter yet because we can't read blocks directly yet. So we don't split actually
                var audioMessage = new AudioMessage(index, clientEndOffset, chunk);
                await entryChannel.Writer.WriteAsync(audioMessage, cancellationToken);
                

                // if (currentSegmentDuration >= SegmentLength.TotalSeconds && clusters.Count > 0) {
                //     currentSegmentDuration = 0;
                //     
                //     await _transcriber.EndTranscription(
                //         new EndTranscriptionCommand(transcriptId),
                //         CancellationToken.None);
                //     await FlushSegment(
                //         recordingId,
                //         metaData,
                //         new WebMDocument(ebml!, segment!, clusters),
                //         state.CurrentSegment,
                //         lastOffset);
                //     await _streamingService.Complete(state.StreamId, cancellationToken);
                //     state.StartNextSegment();
                //     
                //     transcriptId = await _transcriber.BeginTranscription(command, cancellationToken);
                //     _ = DistributeTranscriptionResults(transcriptId, state.StreamId, cancellationToken);
                //     
                //     clusters.Clear();
                // }
                
                lastOffset = clientEndOffset;
                // await Task.WhenAll(distributeChunk, transcribeChunk);
                
                // TODO(AK): we should read blocks there
                (IReadOnlyList<Cluster>,WebMReader.State) ReadClusters(WebMReader webMReader)
                {
                    var result = new List<Cluster>(1);
                    while (webMReader.Read())
                        switch (webMReader.EbmlEntryType) {
                            case EbmlEntryType.None:
                                throw new InvalidOperationException();
                            case EbmlEntryType.Ebml:
                                // TODO: add support of EBML Stream where multiple headers and segments can appear
                                ebml = (EBML?)webMReader.Entry;
                                break;
                            case EbmlEntryType.Segment:
                                segment = (Segment?)webMReader.Entry;
                                break;
                            case EbmlEntryType.Cluster:
                                result.Add((Cluster)webMReader.Entry);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                    return (result, webMReader.GetState());
                }
            }

            entryChannel.Writer.Complete();
            // if (readerState.Container is Cluster c) clusters.Add(c);
            // await _transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId), CancellationToken.None);
            // await _streamingService.Complete(state.StreamId, CancellationToken.None);
            // await FlushSegment(recordingId, metaData, new WebMDocument(ebml!, segment!, clusters), state.CurrentSegment, lastOffset);
        }

        private string GetStreamId(RecordingId id, int index) => $"{id.Value}-{index:D4}";
    }
}