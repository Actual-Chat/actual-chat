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
    [RegisterComputeService(typeof(IAudioRecorder))]
    public class AudioRecorder : DbServiceBase<AudioDbContext>, IAudioRecorder
    {
        private static readonly TimeSpan CleanupInterval = new TimeSpan(0, 0, 10);
        private static readonly TimeSpan SegmentLength = new TimeSpan(0, 1, 0);
        private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new RecyclableMemoryStreamManager();

        private readonly IAuthService _authService;
        private readonly IBlobStorageProvider _blobStorageProvider;

        private readonly Generator<string> _idGenerator;
        // AY:
        // - ConcurrentQueue is a bit of excess here, I guess - a normal queue would be fine
        // - It requires robust maintenance (mem leaks are easy to happen here), so
        //   prob. it's ok to just write for now
        private readonly ConcurrentDictionary<Symbol, (Channel<AppendAudioCommand>, Task)> _dataCapacitor;
        

        public AudioRecorder(IServiceProvider services, IAuthService authService, IBlobStorageProvider blobStorageProvider) : base(services)
        {
            _authService = authService;
            _blobStorageProvider = blobStorageProvider;
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
            Log.LogInformation($"{nameof(Initialize)}, RecordingId = {{RecordingId}}", recordingId);
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
            Log.LogTrace($"{nameof(AppendAudio)}, RecordingId = {{RecordingId}}, Index = {{Index}}, DataLength = {{DataLength}}",
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
            await using var buffer = MemoryStreamManager.GetStream(nameof(AudioRecorder));
            await foreach (var (_, index, clientEndOffset, base64Encoded) in channel.Reader.ReadAllAsync()) {
                var currentOffset = clientEndOffset.ToUnixEpoch();
                metaData.Add(new SegmentMetaDataEntry(index, lastOffset, currentOffset - lastOffset));
                lastOffset = currentOffset;
                await buffer.WriteAsync(base64Encoded.Data);
            }

            buffer.Position = 0;
            var blobId = $"{BlobScope.AudioRecording}{BlobId.ScopeDelimiter}{recordingId}{BlobId.ScopeDelimiter}{Ulid.NewUlid().ToString()}";
            var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecording);
            var persistBlob = blobStorage.WriteAsync(blobId, buffer);
            var persistSegment = PersistSegment();

            async Task PersistSegment()
            {
                await using var dbContext = CreateDbContext(true);
                
                Log.LogInformation($"{nameof(FlushBufferedSegments)}, RecordingId = {{RecordingId}}", recordingId);
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
