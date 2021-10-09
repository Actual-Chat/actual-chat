using ActualChat.Blobs;

namespace ActualChat.Audio;

public interface ISourceAudioRecorder
{
    Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        CancellationToken cancellationToken);
}
