namespace ActualChat.Audio.Processing;

public sealed class AudioSegmentSaver : AudioProcessorBase
{
    private IBlobStorageProvider Blobs { get; }

    public AudioSegmentSaver(IServiceProvider services) : base(services)
        => Blobs = Services.GetRequiredService<IBlobStorageProvider>();

    public async Task<string> Save(
        ClosedAudioSegment closedAudioSegment,
        CancellationToken cancellationToken)
    {
        var streamIndex = closedAudioSegment.StreamId.OrdinalReplace($"{closedAudioSegment.AudioRecord.Id}-", "");
        var blobId = BlobPath.Format(BlobScope.AudioRecord, closedAudioSegment.AudioRecord.Id, streamIndex + ".opuss");

        var streamAdapter = new ActualOpusStreamAdapter(Log);
        var audioSource = closedAudioSegment.Audio;
        var byteStream = streamAdapter.Write(audioSource, cancellationToken);
        var blobStorage = Blobs.GetBlobStorage(BlobScope.AudioRecord);
        await blobStorage.UploadByteStream(blobId, byteStream, cancellationToken).ConfigureAwait(false);
        return blobId;
    }
}
