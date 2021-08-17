using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio.Db;
using ActualChat.Audio.Ebml;
using ActualChat.Audio.Ebml.Models;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Stl.Async;
using Stl.Channels;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Generators;
using Stl.Serialization;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Audio
{
    public class AudioRecorder : DbServiceBase<AudioDbContext>, IAudioRecorder
    {
        private static readonly TimeSpan CleanupInterval = new TimeSpan(0, 0, 10);
        private static readonly TimeSpan SegmentLength = new TimeSpan(0, 1, 0);
        private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new RecyclableMemoryStreamManager();

        private readonly IAuthService _authService;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ILogger<AudioRecorder> _log;

        private readonly Generator<string> _idGenerator;
        // AY:
        // - ConcurrentQueue is a bit of excess here, I guess - a normal queue would be fine
        // - It requires robust maintenance (mem leaks are easy to happen here), so
        //   prob. it's ok to just write for now
        private readonly ConcurrentDictionary<Symbol, (Channel<AppendAudioCommand>, Task)> _dataCapacitor;
        

        public AudioRecorder(IServiceProvider services, IAuthService authService, IBlobStorageProvider blobStorageProvider, ILogger<AudioRecorder> log) : base(services)
        {
            _authService = authService;
            _blobStorageProvider = blobStorageProvider;
            _log = log;
            _idGenerator = new RandomStringGenerator(16, RandomStringGenerator.Base32Alphabet);
            _dataCapacitor = new ConcurrentDictionary<Symbol, (Channel<AppendAudioCommand>, Task)>();
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
            
            var channel = Channel.CreateUnbounded<AppendAudioCommand>(new UnboundedChannelOptions{ SingleReader = true });
            _dataCapacitor.TryAdd(recordingId, (channel, FlushBufferedSegments(recordingId, command.ClientStartOffset, channel)));

            return recordingId;
        }

        public virtual async Task AppendAudio(AppendAudioCommand command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return;

            var (recordingId, index, offset, data) = command;
            _log.LogTrace($"{nameof(AppendAudio)}, RecordingId = {{RecordingId}}, Index = {{Index}}, DataLength = {{DataLength}}",
                recordingId.Value,
                command.Index,
                command.Data.Count);

            // Push to real-time pipeline
            // TBD

            // Waiting for Initialize
            var waitAttempts = 0;
            while (!_dataCapacitor.ContainsKey(recordingId) && waitAttempts < 5) {
                await Task.Delay(10, cancellationToken);
                waitAttempts++;
            }
            
            // Initialize hasn't been completed or Recording has already been completed
            if (!_dataCapacitor.TryGetValue(recordingId, out var tuple)) return;

            var (channel, _) = tuple;
            await channel.Writer.WriteAsync(command, cancellationToken);;
        }

        public virtual async Task Complete(CompleteAudioRecording command, CancellationToken cancellationToken = default)
        {
            if (Computed.IsInvalidating()) return;

            if (_dataCapacitor.TryRemove(command.RecordingId, out var tuple)) {
                var (channel, flushTask) = tuple;
                channel.Writer.Complete();
                await flushTask.WithFakeCancellation(cancellationToken);
            }
        }

        private async Task FlushBufferedSegments(
            Symbol recordingId, 
            Moment recordingStartOffset,
            Channel<AppendAudioCommand> channel)
        {
            var lastOffset = recordingStartOffset.ToUnixEpoch();
            var metaData = new List<SegmentMetaDataEntry>();
            EBML? ebml = null; 
            Segment? segment = null;
            
            // await using var buffer = MemoryStreamManager.GetStream(nameof(AudioRecorder));
            await foreach (var (_, index, clientEndOffset, base64Encoded) in channel.Reader.ReadAllAsync()) {
                var currentOffset = clientEndOffset.ToUnixEpoch();
                metaData.Add(new SegmentMetaDataEntry(index, lastOffset, currentOffset - lastOffset));
                lastOffset = currentOffset;
                // await buffer.WriteAsync(base64Encoded.Data);
                void Parse(byte[] data)
                {
                    var reader = new EbmlReader(data);
                    var clusters = new List<Cluster>(1);
                    while (reader.Read())
                        switch (reader.EbmlEntryType) {
                            case EbmlEntryType.None:
                                throw new InvalidOperationException();
                            case EbmlEntryType.EBML:
                                ebml = (EBML?)reader.Entry;
                                break;
                            case EbmlEntryType.Segment:
                                segment = (Segment?)reader.Entry;
                                break;
                            case EbmlEntryType.Cluster:
                                clusters.Add((Cluster)reader.Entry);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    
                }
            }

            // buffer.Position = 0;
            var blobId = $"{BlobScope.AudioRecording}{BlobId.ScopeDelimiter}{recordingId}{BlobId.ScopeDelimiter}{Ulid.NewUlid().ToString()}";
            var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecording);
            var persistBlob = blobStorage.WriteAsync(blobId, null);
            var persistSegment = PersistSegment();

            async Task PersistSegment()
            {
                await using var dbContext = CreateDbContext(true);
                
                _log.LogInformation($"{nameof(FlushBufferedSegments)}, RecordingId = {{RecordingId}}", recordingId);
                dbContext.AudioSegments.Add(new DbAudioSegment {
                    RecordingId = recordingId,
                    Index = 0,
                    Offset = 0d,
                    Duration = metaData.Sum(md => md.Duration),
                    BlobId = blobId,
                    BlobMetadata = JsonSerializer.Serialize(metaData)
                });
                await dbContext.SaveChangesAsync();
            }

            await Task.WhenAll(persistBlob, persistSegment);
        }

        private record SegmentMetaDataEntry(int Index, double Offset, double Duration);

    }
}
