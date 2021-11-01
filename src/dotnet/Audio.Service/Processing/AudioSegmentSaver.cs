using ActualChat.Audio.Db;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Audio.Processing;

public sealed class AudioSegmentSaver : DbServiceBase<AudioDbContext>
{
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

    private readonly IBlobStorageProvider _blobStorageProvider;
    private readonly ILogger<AudioSegmentSaver> _log;

    public AudioSegmentSaver(
        IServiceProvider services,
        IBlobStorageProvider blobStorageProvider,
        ILogger<AudioSegmentSaver>? log = null)
        : base(services)
    {
        _blobStorageProvider = blobStorageProvider;
        _log = log ?? NullLogger<AudioSegmentSaver>.Instance;
    }

    public async Task<string> Save(
        AudioSegment audioSegment,
        CancellationToken cancellationToken)
    {
        var p = audioSegment ?? throw new ArgumentNullException(nameof(audioSegment));
        var streamIndex = ((string) p.StreamId).Replace($"{p.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, p.AudioRecord.Id, streamIndex + ".webm");

        var saveBlobTask = SaveBlob(blobId, audioSegment.AudioSource, cancellationToken);
        var saveSegmentTask = SaveSegment();
        await Task.WhenAll(saveBlobTask, saveSegmentTask);
        return blobId;

        async Task SaveSegment() {
            await using var dbContext = CreateDbContext(true);
            var existingRecord = await dbContext.AudioRecords.FindAsync(
                ComposeKey(p.AudioRecord.Id.Value),
                cancellationToken);

            if (existingRecord == null) {
                _log.LogInformation("Entity = Record, RecordId = {RecordId}", p.AudioRecord.Id);
                dbContext.AudioRecords.Add(new DbAudioRecord {
                    Id = p.AudioRecord.Id,
                    AuthorId = p.AudioRecord.AuthorId,
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
        OpenAudioSegment openAudioSegment,
        CancellationToken cancellationToken)
    {
        var p = openAudioSegment ?? throw new ArgumentNullException(nameof(openAudioSegment));
        var streamIndex = ((string) p.StreamId).Replace($"{p.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, p.AudioRecord.Id, streamIndex + ".webm");

        await SaveBlob(blobId, openAudioSegment.Source, cancellationToken).ConfigureAwait(false);
        return blobId;
    }

    private async Task SaveBlob(
        string blobId,
        AudioSource source,
        CancellationToken cancellationToken)
    {
        var blobStorage = _blobStorageProvider.GetBlobStorage(BlobScope.AudioRecord);
        await using var stream = MemoryStreamManager.GetStream(nameof(AudioSegmentSaver));
        var header = Convert.FromBase64String(source.Format.CodecSettings);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await foreach (var audioFrame in source.Frames.WithCancellation(cancellationToken))
            await stream.WriteAsync(audioFrame.Data, cancellationToken).ConfigureAwait(false);

        stream.Position = 0;
        await blobStorage.WriteAsync(blobId, stream, append: false, cancellationToken).ConfigureAwait(false);
    }
}
