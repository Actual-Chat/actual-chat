using System;
using System.Buffers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Audio.Db;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.Blobs;
using ActualChat.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Audio.Orchestration
{
    public sealed class AudioPersistService
    {
        private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();
        
        private readonly IDbContextFactory<AudioDbContext> _dbContextFactory;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ILogger<AudioPersistService> _log;

        public AudioPersistService(IDbContextFactory<AudioDbContext> dbContextFactory, IBlobStorageProvider blobStorageProvider, ILogger<AudioPersistService> log)
        {
            _dbContextFactory = dbContextFactory;
            _blobStorageProvider = blobStorageProvider;
            _log = log;
        }

        public async Task<string> Persist(
            AudioEntry audioEntry,
            CancellationToken cancellationToken)
        {
            if (audioEntry == null) throw new ArgumentNullException(nameof(audioEntry));
            var (index, streamId, (recordingId, _), document, metaData, offset, duration) = audioEntry;
            var blobId = $"{BlobScope.AudioRecording}{BlobId.ScopeDelimiter}{recordingId.Value}{BlobId.ScopeDelimiter}{streamId}";
            var persistBlob = PersistBlob(recordingId, blobId, document, cancellationToken);
            var persistSegment = PersistSegment(cancellationToken);
            
            await Task.WhenAll(persistBlob, persistSegment);
            return blobId;

            async Task PersistSegment(CancellationToken ct)
            {
                await using var dbContext = _dbContextFactory.CreateDbContext().ReadWrite();
                
                _log.LogInformation($"{nameof(AudioPersistService)}, RecordingId = {{RecordingId}}", recordingId.Value);
                dbContext.AudioSegments.Add(new DbAudioSegment {
                    RecordingId = recordingId.Value,
                    Index = index,
                    Offset = offset,
                    Duration = duration,
                    BlobId = blobId,
                    BlobMetadata = JsonSerializer.Serialize(metaData)
                });
                await dbContext.SaveChangesAsync(ct);
            }
        }

        private async Task PersistBlob(
            RecordingId id,
            string blobId,
            WebMDocument document,
            CancellationToken cancellationToken)
        {
            const int minBufferSize = 32*1024;
            if (!document.IsValid) _log.LogWarning("Skip flushing audio segments for {RecordingId}. WebM document is invalid", id.Value);

            var (ebml, segment, clusters) = document;
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
            await blobStorage.WriteAsync(blobId, stream, false, cancellationToken);
            
            bool WriteEntry(WebMWriter writer, RootEntry entry)
            {
                if (!writer.Write(entry))
                    return false;
                
                stream?.Write(writer.Written);
                return true;
            }
        }
    }
}