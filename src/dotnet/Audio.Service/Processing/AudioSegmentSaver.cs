namespace ActualChat.Audio.Processing;

public sealed class AudioSegmentSaver : AudioProcessorBase
{
    private IBlobStorages Blobs { get; }

    public AudioSegmentSaver(IServiceProvider services) : base(services)
        => Blobs = services.GetRequiredService<IBlobStorages>();

    public async Task<string> Save(
        ClosedAudioSegment closedAudioSegment,
        CancellationToken cancellationToken)
    {
        var streamIndex = closedAudioSegment.StreamId.OrdinalReplace($"{closedAudioSegment.AudioRecord.Id}-", "");
        var blobId = BlobPath.Format(BlobScope.AudioRecord, closedAudioSegment.AudioRecord.Id, streamIndex + ".opuss");

        var converter = new ActualOpusStreamConverter(Clocks, Log);
        var audioSource = closedAudioSegment.Audio;
        var byteStream = converter.ToByteStream(audioSource, cancellationToken);
        var blobStorage = Blobs[BlobScope.AudioRecord];
        await blobStorage.UploadByteStream(blobId, byteStream, cancellationToken).ConfigureAwait(false);
        return blobId;
    }
}
