using ActualChat.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Audio.Processing;

public sealed class AudioSegmentSaver
{
    private IBlobStorageProvider Blobs { get; }
    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    private ILogger<AudioSegmentSaver> Log { get; }

    public AudioSegmentSaver(
        IBlobStorageProvider blobs,
        ILogger<AudioSegmentSaver>? log = null)
    {
        Blobs = blobs;
        Log = log ?? NullLogger<AudioSegmentSaver>.Instance;
    }

    public async Task<string> Save(
        ClosedAudioSegment closedAudioSegment,
        CancellationToken cancellationToken)
    {
        var streamIndex = closedAudioSegment.StreamId.Replace(
            $"{closedAudioSegment.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, closedAudioSegment.AudioRecord.Id, streamIndex + ".webm");

        var audioSource = closedAudioSegment.Audio;
        var audioStream = audioSource.GetFrames(cancellationToken);
        var byteStream = audioStream.ToByteStream(audioSource.Format, cancellationToken);
        var blobStorage = Blobs.GetBlobStorage(BlobScope.AudioRecord);
        await blobStorage.UploadByteStream(blobId, byteStream, cancellationToken).ConfigureAwait(false);
        return blobId;
    }
}
