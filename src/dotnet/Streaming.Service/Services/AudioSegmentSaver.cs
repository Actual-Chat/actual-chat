using ActualChat.Audio;

namespace ActualChat.Streaming.Services;

public sealed class AudioSegmentSaver(IServiceProvider services) : AudioProcessorBase(services)
{
    private IBlobStorages Blobs { get; } = services.GetRequiredService<IBlobStorages>();

    public async Task<string> Save(
        ClosedAudioSegment closedAudioSegment,
        CancellationToken cancellationToken)
    {
        var streamIndex = closedAudioSegment.StreamId.OrdinalReplace($"{closedAudioSegment.AudioRecord.StreamId}-", "");
        var blobId = BlobPath.Format(BlobScope.AudioRecord, closedAudioSegment.AudioRecord.StreamId, streamIndex + ".opuss");

        var converter = new ActualOpusStreamConverter(Clocks, Log);
        var audioSource = closedAudioSegment.Audio;
        var byteStream = converter.ToByteStream(audioSource, cancellationToken);
        var blobStorage = Blobs[BlobScope.AudioRecord];
        await blobStorage.UploadByteStream(blobId, byteStream, cancellationToken).ConfigureAwait(false);
        return blobId;
    }
}
