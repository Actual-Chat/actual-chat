using ActualChat.Blobs;

namespace ActualChat.Audio;

public interface ISourceAudioRecorder
{
    Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobStream,
        CancellationToken cancellationToken);
}
