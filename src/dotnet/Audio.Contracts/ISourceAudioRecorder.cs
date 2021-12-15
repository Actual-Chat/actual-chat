namespace ActualChat.Audio;

public interface ISourceAudioRecorder
{
    Task RecordSourceAudio(
        Session session,
        AudioRecord record,
        IAsyncEnumerable<BlobPart> blobStream,
        CancellationToken cancellationToken);
}
