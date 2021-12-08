namespace ActualChat.Audio;

public interface IAudioStreamer
{
    public IAsyncEnumerable<BlobPart> GetAudioBlobStream(string streamId, CancellationToken cancellationToken);
}
