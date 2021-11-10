using ActualChat.Audio.Db;
using ActualChat.Blobs;
using ActualChat.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Audio.Processing;

public sealed class AudioSegmentSaver
{
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

    private readonly IBlobStorageProvider _blobs;
    private readonly ILogger<AudioSegmentSaver> _log;

    public AudioSegmentSaver(
        IServiceProvider services,
        IBlobStorageProvider blobs,
        ILogger<AudioSegmentSaver>? log = null)
    {
        _blobs = blobs;
        _log = log ?? NullLogger<AudioSegmentSaver>.Instance;
    }

    public async Task<string> Save(
        ClosedAudioSegment closedAudioSegment,
        CancellationToken cancellationToken)
    {
        var streamIndex = ((string)closedAudioSegment.StreamId).Replace(
            $"{closedAudioSegment.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, closedAudioSegment.AudioRecord.Id, streamIndex + ".webm");

        var audioStream = closedAudioSegment.GetSegmentStream(cancellationToken);
        var blobStream = audioStream.ToBlobStream(cancellationToken);
        var blobStorage = _blobs.GetBlobStorage(BlobScope.AudioRecord);
        await blobStorage.UploadBlobStream(blobId, blobStream, cancellationToken).ConfigureAwait(false);
        return blobId;
    }
}
