using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Distribution;
using ActualChat.Transcription;

namespace ActualChat.Audio.Orchestration
{
    public sealed class AudioStreamSplitter
    {
        public AudioStreamSplitter()
        {
        }

        public async IAsyncEnumerable<AudioStreamEntry> SplitBySilencePeriods(
            AudioRecording audioRecording,
            ChannelReader<AudioRecordMessage> audioReader,
            [EnumeratorCancellation]CancellationToken cancellationToken)
        {
            var lastOffset = audioRecording.Configuration.ClientStartOffset;
            var metaData = new List<MetaDataEntry>();
            EBML? ebml = null; 
            Segment? segment = null;
            WebMReader.State readerState = new WebMReader.State();
            var clusters = new List<Cluster>();
            var currentSegmentDuration = 0d;
            using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
            await foreach (var (index, clientEndOffset, chunk) in audioReader.ReadAllAsync(cancellationToken)) {
                var duration = clientEndOffset - lastOffset;
                metaData.Add(new MetaDataEntry(index, lastOffset, duration));
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
            
            if (readerState.Container is Cluster c) clusters.Add(c);

            // await _transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId), CancellationToken.None);
            // await _streamingService.Complete(state.StreamId, CancellationToken.None);
            // await FlushSegment(recordingId, metaData, new WebMDocument(ebml!, segment!, clusters), state.CurrentSegment, lastOffset);
            
            yield break;

            throw new NotImplementedException();
        }
    }
}