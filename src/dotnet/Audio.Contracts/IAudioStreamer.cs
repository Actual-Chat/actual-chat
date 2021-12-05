namespace ActualChat.Audio;

public interface IAudioStreamer
{
    public IAsyncEnumerable<BlobPart> GetAudioBlobStream(StreamId streamId, CancellationToken cancellationToken);
}
