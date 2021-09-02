using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.Db;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using ActualChat.Distribution;
using ActualChat.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Generators;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Audio
{
    public class AudioRecorder : DbServiceBase<AudioDbContext>, IAudioRecorder
    {
        private static readonly TimeSpan SegmentLength = new TimeSpan(0, 0, 10);
        private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

        private readonly IAuthService _authService;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ITranscriber _transcriber;
        private readonly IServerSideAudioStreamingService _streamingService;
        private readonly IServerSideStreamingService<TranscriptMessage> _transcriptStreamingService;
        private readonly ILogger<AudioRecorder> _log;

        private readonly Generator<string> _idGenerator;
        private readonly ConcurrentDictionary<Symbol, RecordingState> _dataCapacitor;
        

        public AudioRecorder(
            IServiceProvider services,
            IAuthService authService,
            IBlobStorageProvider blobStorageProvider,
            ITranscriber transcriber,
            IServerSideAudioStreamingService streamingService,
            IServerSideStreamingService<TranscriptMessage> transcriptStreamingService,
            ILogger<AudioRecorder> log) : base(services)
        {
            _authService = authService;
            _blobStorageProvider = blobStorageProvider;
            _transcriber = transcriber;
            _streamingService = streamingService;
            _transcriptStreamingService = transcriptStreamingService;
            _log = log;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
            _dataCapacitor = new ConcurrentDictionary<Symbol,RecordingState>();
        }

        public virtual async Task<Symbol> Initialize(InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return default!;

            var user = await _authService.GetUser(command.Session, cancellationToken);
            user.MustBeAuthenticated();

            await using var dbContext = await CreateCommandDbContext(cancellationToken);

            var recordingId = _idGenerator.Next();
            _log.LogInformation($"{nameof(Initialize)}, RecordingId = {{RecordingId}}", recordingId);
            dbContext.AudioRecordings.Add(new DbAudioRecording {
                Id = recordingId,
                UserId = user.Id,
                RecordingStartedUtc = command.ClientStartOffset,
                AudioCodec = command.AudioFormat.Codec,
                ChannelCount = command.AudioFormat.ChannelCount,
                SampleRate = command.AudioFormat.SampleRate,
                Language = command.Language,
                RecordingDuration = 0
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            
            // var channel = Channel.CreateUnbounded<AppendAudioCommand>(new UnboundedChannelOptions{ SingleReader = true });
            var recordingState = new RecordingState(recordingId);
            recordingState.ProcessAudioTask = ProcessAudioStream(recordingId, command.ClientStartOffset, recordingState);
            _dataCapacitor.TryAdd(recordingId, recordingState);

            return recordingId;
        }

        // public virtual async Task AppendAudio(AppendAudioCommand command, CancellationToken cancellationToken = default)
        // {
        //     if (Computed.IsInvalidating()) return;
        //
        //     var (recordingId, index, _, data) = command;
        //     _log.LogTrace($"{nameof(AppendAudio)}, RecordingId = {{RecordingId}}, Index = {{Index}}, DataLength = {{DataLength}}",
        //         recordingId.Value,
        //         index,
        //         data.Count);
        //
        //     
        //     // Waiting for Initialize
        //     var waitAttempts = 0;
        //     while (!_dataCapacitor.ContainsKey(recordingId) && waitAttempts < 5) {
        //         await Task.Delay(10, cancellationToken);
        //         waitAttempts++;
        //     }
        //     
        //     // Initialize hasn't been completed or Recording has already been completed
        //     if (!_dataCapacitor.TryGetValue(recordingId, out var state)) return;
        //
        //     var channel = state.AudioInput;
        //     await channel.Writer.WriteAsync(command, cancellationToken);
        // }

        public virtual async Task Complete(CompleteAudioRecording command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return;

            if (_dataCapacitor.TryRemove(command.RecordingId, out var state)) {
                state.CancelProcessing();
                try {
                    await state.ProcessAudioTask;
                }
                catch (TaskCanceledException) { }
            }
        }

        private async Task ProcessAudioStream(
            Symbol recordingId, 
            Moment recordingStartOffset,
            RecordingState state)
        {
            // var channel = state.AudioInput;
            var cancellationToken = state.CancellationToken;
            var reader = await _streamingService.GetStream(recordingId.Value, cancellationToken);
            
            var lastOffset = recordingStartOffset.ToUnixEpoch();
            var metaData = new List<SegmentMetaDataEntry>();
            EBML? ebml = null; 
            Segment? segment = null;
            WebMReader.State readerState = new WebMReader.State();
            var clusters = new List<Cluster>();
            var currentSegmentDuration = 0d;

            // TODO: AK - read actual config
            var command = new BeginTranscriptionCommand {
                RecordingId = recordingId,
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                Options = new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                }
            };
            var transcriptId = await _transcriber.BeginTranscription(command, cancellationToken);
            _ = DistributeTranscriptionResults(transcriptId, state.StreamId, cancellationToken);
            
            using var bufferLease = MemoryPool<byte>.Shared.Rent(32 * 1024);
            await foreach (var (index, clientEndOffset, chunk) in reader.ReadAllAsync(cancellationToken)) {
                var streamId = state.StreamId;
                var message = new AudioMessage(index, clientEndOffset, chunk);
                // Push to real-time pipeline
                var distributeChunk = _streamingService.Publish(streamId, message, cancellationToken);
                var appendTranscriptionCommand = new AppendTranscriptionCommand(transcriptId, chunk);
                var transcribeChunk = _transcriber.AppendTranscription(appendTranscriptionCommand, cancellationToken);
                
                
                var duration = clientEndOffset - lastOffset;
                metaData.Add(new SegmentMetaDataEntry(index, lastOffset, duration));
                var remainingLength = readerState.Remaining;
                var buffer = bufferLease.Memory;
                // TODO: AK - check length!!!
                
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

                if (currentSegmentDuration >= SegmentLength.TotalSeconds && clusters.Count > 0) {
                    currentSegmentDuration = 0;
                    
                    await _transcriber.EndTranscription(
                        new EndTranscriptionCommand(transcriptId),
                        CancellationToken.None);
                    await FlushSegment(
                        recordingId,
                        metaData,
                        new WebMDocument(ebml!, segment!, clusters),
                        state.CurrentSegment,
                        lastOffset);
                    await _streamingService.Complete(state.StreamId, cancellationToken);
                    state.StartNextSegment();
                    
                    transcriptId = await _transcriber.BeginTranscription(command, cancellationToken);
                    _ = DistributeTranscriptionResults(transcriptId, state.StreamId, cancellationToken);
                    
                    clusters.Clear();
                }
                
                lastOffset = clientEndOffset;
                await Task.WhenAll(distributeChunk, transcribeChunk);
                
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

            await _transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId), CancellationToken.None);
            await _streamingService.Complete(state.StreamId, CancellationToken.None);
            await FlushSegment(recordingId, metaData, new WebMDocument(ebml!, segment!, clusters), state.CurrentSegment, lastOffset);
        }

        private async Task DistributeTranscriptionResults(Symbol transcriptId, string streamId, CancellationToken cancellationToken)
        {
            var channel = Channel.CreateBounded<TranscriptMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var publishTask = _transcriptStreamingService.PublishStream(streamId, channel.Reader, cancellationToken);

            _ = PollInternal(channel.Writer, cancellationToken);

            await publishTask;

            async Task PollInternal(ChannelWriter<TranscriptMessage> writer, CancellationToken ct)
            {
                var index = 0;
                var result = await _transcriber.PollTranscription(new PollTranscriptionCommand(transcriptId, index), ct);
                while (result.ContinuePolling && !ct.IsCancellationRequested) {
                    foreach (var fragmentVariant in result.Fragments) {
                        if (fragmentVariant.Speech is { } speechFragment) {
                            var message = new TranscriptMessage(
                                speechFragment.Text,
                                speechFragment.TextIndex,
                                speechFragment.StartOffset,
                                speechFragment.Duration);
                            await writer.WriteAsync(message, ct);
                        }
                        else if (fragmentVariant.Error != null) {
                            // TODO: AK - think about additional scenarios of transription error handling
                        }

                        index = fragmentVariant.Value!.Index + 1;
                    }

                    result = await _transcriber.PollTranscription(
                        new PollTranscriptionCommand(transcriptId, index),
                        ct);
                }

                writer.Complete();
                await _transcriber.AckTranscription(new AckTranscriptionCommand(transcriptId, index), ct);
            }
        }

        private async Task FlushSegment(Symbol recordingId, IReadOnlyList<SegmentMetaDataEntry> metaData, WebMDocument webM, int index, double offset)
        {
            if (metaData == null) throw new ArgumentNullException(nameof(metaData));
            if (webM == null) throw new ArgumentNullException(nameof(webM));
            if (!webM.IsValid) {
                _log.LogWarning("Skip flushing audio segments for {{RecordingId}}. WebM document is invalid", recordingId);
                return;
            }

            var (ebml, segment, clusters) = webM;
            var minBufferSize = 32*1024;
            var blobId = $"{BlobScope.AudioRecording}{BlobId.ScopeDelimiter}{recordingId}{BlobId.ScopeDelimiter}{Ulid.NewUlid().ToString()}";
            var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecording);
            await using var stream = MemoryStreamManager.GetStream(nameof(AudioRecorder));
            using var bufferLease = MemoryPool<byte>.Shared.Rent(minBufferSize);

            var ebmlWritten = WriteEntry(new WebMWriter(bufferLease.Memory.Span), ebml);
            var segmentWritten = WriteEntry(new WebMWriter(bufferLease.Memory.Span), segment);
            if (!ebmlWritten)
                throw new InvalidOperationException("Can't write EBML entry");
            if (!segmentWritten)
                throw new InvalidOperationException("Can't write Segment entry");

            foreach (var cluster in clusters) {
                var memory = bufferLease.Memory;
                var clusterWritten = WriteEntry(new WebMWriter(memory.Span), cluster);
                if (clusterWritten) continue;
                
                var cycleNumber = 0;
                var bufferSize = minBufferSize;
                while (true) {
                    bufferSize *= 2;
                    cycleNumber++;
                    using var extendedBufferLease = MemoryPool<byte>.Shared.Rent(bufferSize);
                    if (WriteEntry(new WebMWriter(extendedBufferLease.Memory.Span), cluster))
                        break;
                    if (cycleNumber >= 10)
                        break;
                }
            }

            stream.Position = 0;
            var persistBlob = blobStorage.WriteAsync(blobId, stream);
            var persistSegment = PersistSegment();

            bool WriteEntry(WebMWriter writer, RootEntry entry)
            {
                if (!writer.Write(entry))
                    return false;
                
                stream?.Write(writer.Written);
                return true;
            }

            async Task PersistSegment()
            {
                await using var dbContext = CreateDbContext(true);
                
                _log.LogInformation($"{nameof(ProcessAudioStream)}, RecordingId = {{RecordingId}}", recordingId);
                dbContext.AudioSegments.Add(new DbAudioSegment {
                    RecordingId = recordingId,
                    Index = index,
                    Offset = offset,
                    Duration = metaData.Sum(md => md.Duration),
                    BlobId = blobId,
                    BlobMetadata = JsonSerializer.Serialize(metaData)
                });
                await dbContext.SaveChangesAsync();
            }

            await Task.WhenAll(persistBlob, persistSegment);
        }

        private record SegmentMetaDataEntry(int Index, double Offset, double Duration);

        private class RecordingState
        {
            private readonly Symbol _recordingId;
            private readonly CancellationTokenSource _cts;

            public RecordingState(Symbol recordingId)
            {
                _recordingId = recordingId;
                _cts = new CancellationTokenSource();
                
                CurrentSegment = 0;
            }

            public Task ProcessAudioTask { get; set; } = null!;

            public CancellationToken CancellationToken => _cts.Token;

            public int CurrentSegment { get; private set; }

            public string StreamId => $"{_recordingId}-{CurrentSegment:D4}";

            public void StartNextSegment()
            {
                CurrentSegment++;
            }

            public void CancelProcessing()
            {
                _cts.Cancel();
            }

            public void Deconstruct(out Task processTask, out int currentSegment, out CancellationToken cancellationToken)
            {
                processTask = ProcessAudioTask;
                currentSegment = CurrentSegment;
                cancellationToken = CancellationToken;
            }
        } 
    }
}
