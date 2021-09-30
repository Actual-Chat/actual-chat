using ActualChat.Blobs;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio;

public interface ISourceAudioRecorder
{
    Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        ChannelReader<BlobPart> content,
        CancellationToken cancellationToken);
}
