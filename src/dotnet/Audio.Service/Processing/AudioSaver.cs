using ActualChat.Audio.Db;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Audio.Processing;

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
        var streamIndex = ((string) p.StreamId).Replace($"{p.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, p.AudioRecord.Id, streamIndex + ".webm");

        var saveBlobTask = SaveBlob(blobId, audioStreamPart.AudioSource, cancellationToken);
        var saveSegmentTask = SaveSegment();
        await Task.WhenAll(saveBlobTask, saveSegmentTask);
        return blobId;

        async Task SaveSegment() {
            await using var dbContext = CreateDbContext(true);
            var existingRecording = await dbContext.AudioRecords.FindAsync(
                ComposeKey(p.AudioRecord.Id.Value),
                cancellationToken);
            if (existingRecording == null) {
                _log.LogInformation("Entity = Record, RecordId = {RecordId}", p.AudioRecord.Id);
                dbContext.AudioRecords.Add(new DbAudioRecord {
                    Id = p.AudioRecord.Id,
                    UserId = p.AudioRecord.UserId,
                    ChatId = p.AudioRecord.ChatId,
                    // TODO(AK): fill record entity attributes
                    BeginsAt = default,
                    Duration = 0,
                    AudioCodecKind = p.AudioRecord.Format.CodecKind,
                    ChannelCount = p.AudioRecord.Format.ChannelCount,
                    SampleRate = p.AudioRecord.Format.SampleRate,
                    Language = p.AudioRecord.Language
                });
            }

            _log.LogInformation("Entity = AudioSegment, RecordId = {RecordId}, Index = {Index}", p.AudioRecord.Id, p.Index);
            dbContext.AudioSegments.Add(new DbAudioSegment {
                RecordId = p.AudioRecord.Id,
                Index = p.Index,
                Offset = p.Offset,
                Duration = p.Duration,
                BlobId = blobId,
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string> Save(
        AudioRecordSegment audioRecordSegment,
        CancellationToken cancellationToken)
    {
        var p = audioRecordSegment ?? throw new ArgumentNullException(nameof(audioRecordSegment));
        var streamIndex = ((string) p.StreamId).Replace($"{p.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, p.AudioRecord.Id, streamIndex + ".webm");

        await SaveBlob(blobId, audioRecordSegment.Source, cancellationToken).ConfigureAwait(false);
        return blobId;
    }

    private async Task SaveBlob(
        string blobId,
        AudioSource source,
        CancellationToken cancellationToken)
    {
        var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecord);
        await using var stream = MemoryStreamManager.GetStream(nameof(AudioSaver));
        var header = Convert.FromBase64String(source.Format.CodecSettings);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await foreach (var audioFrame in source.Frames.WithCancellation(cancellationToken))
            await stream.WriteAsync(audioFrame.Data, cancellationToken).ConfigureAwait(false);

        stream.Position = 0;
        await blobStorage.WriteAsync(blobId, stream, append: false, cancellationToken).ConfigureAwait(false);
    }
}
