using System;
using System.Buffers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio.Db;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Audio.Orchestration
{
    public sealed class AudioSaver : DbServiceBase<AudioDbContext>
    {
        private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ILogger<AudioSaver> _log;

        public AudioSaver(
            IServiceProvider services,
            IBlobStorageProvider blobStorageProvider,
            ILogger<AudioSaver>? log = null)
            : base(services)
        {
            _blobStorageProvider = blobStorageProvider;
            _log = log ?? NullLogger<AudioSaver>.Instance;
        }

        public async Task<string> Save(
            AudioStreamPart audioStreamPart,
            CancellationToken cancellationToken)
        {
            var p = audioStreamPart ?? throw new ArgumentNullException(nameof(audioStreamPart));
            var streamIndex = ((string)p.StreamId).Replace($"{p.AudioRecord.Id}-", "");
            var blobId = BlobPath.Format(BlobScope.AudioRecording, p.AudioRecord.Id, streamIndex + ".webm");

            var saveBlobTask = SaveBlob(blobId, audioStreamPart, cancellationToken);
            var saveSegmentTask = SaveSegment();
            await Task.WhenAll(saveBlobTask, saveSegmentTask);
            return blobId;

            async Task SaveSegment() {
                await using var dbContext = CreateDbContext(true);
                var existingRecording = await dbContext.AudioRecordings.FindAsync(new object[] { p.AudioRecord.Id }, cancellationToken);
                if (existingRecording == null) {
                    _log.LogInformation("Entity = Recording, RecordingId = {RecordingId}", p.AudioRecord.Id);
                    dbContext.AudioRecordings.Add(new DbAudioRecording {
                        Id = p.AudioRecord.Id,
                        UserId = p.AudioRecord.UserId,
                        ChatId = p.AudioRecord.ChatId,
                        // TODO(AK): fill recording DB entity attributes
                        RecordingStartedUtc = default,
                        RecordingDuration = 0,
                        AudioCodec = p.AudioRecord.Format.Codec,
                        ChannelCount = p.AudioRecord.Format.ChannelCount,
                        SampleRate = p.AudioRecord.Format.SampleRate,
                        Language = p.AudioRecord.Language
                    });
                }

                _log.LogInformation("Entity = AudioSegment, RecordingId = {RecordingId}, Index = {Index}", p.AudioRecord.Id, p.Index);
                dbContext.AudioSegments.Add(new DbAudioSegment {
                    RecordingId = p.AudioRecord.Id,
                    Index = p.Index,
                    Offset = p.Offset,
                    Duration = p.Duration,
                    BlobId = blobId,
                    BlobMetadata = JsonSerializer.Serialize(p.Metadata)
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<string> Save(
            AudioRecordSegmentAccessor audioRecordSegmentAccessor,
            CancellationToken cancellationToken)
        {
            var p = audioRecordSegmentAccessor ?? throw new ArgumentNullException(nameof(audioRecordSegmentAccessor));
            var streamIndex = ((string)p.StreamId).Replace($"{p.AudioRecord.Id}-", "");
            var blobId = BlobPath.Format(BlobScope.AudioRecording, p.AudioRecord.Id, streamIndex + ".webm");

            await SaveBlob(blobId, audioRecordSegmentAccessor, cancellationToken);
            return blobId;
        }


        private async Task SaveBlob(
            string blobId,
            AudioStreamPart audioStreamPart,
            CancellationToken cancellationToken)
        {
            const int minBufferSize = 32*1024;
            var document = audioStreamPart.Document;
            if (!document.IsValid)
                _log.LogWarning("Skip flushing audio segments for {StreamId}. WebM document is invalid", audioStreamPart.StreamId);

            var (ebml, segment, clusters) = document;
            var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecording);
            await using var stream = MemoryStreamManager.GetStream(nameof(AudioSaver));
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
            await blobStorage.WriteAsync(blobId, stream, false, cancellationToken);

            bool WriteEntry(WebMWriter writer, RootEntry entry) {
                if (!writer.Write(entry))
                    return false;

                stream?.Write(writer.Written);
                return true;
            }
        }
        
        private async Task SaveBlob(
            string blobId,
            AudioRecordSegmentAccessor audioSegmentAccessor,
            CancellationToken cancellationToken)
        {
            const int minBufferSize = 32*1024;
            var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecording);
            await using var stream = MemoryStreamManager.GetStream(nameof(AudioSaver));
            using var bufferLease = MemoryPool<byte>.Shared.Rent(minBufferSize);

            await foreach (var (_, bytes) in audioSegmentAccessor.GetStream().ReadAllAsync(cancellationToken)) 
                stream.Write(bytes);

            stream.Position = 0;
            await blobStorage.WriteAsync(blobId, stream, false, cancellationToken);
        }
    }
}
