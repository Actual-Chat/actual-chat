using ActualChat.Media;

namespace ActualChat.Audio;

public interface ISourceAudioRecorder
{
    Task RecordSourceAudio(
        Session session,
        AudioRecord record,
        IAsyncEnumerable<RecordingPart> recordingStream,
        CancellationToken cancellationToken);
}
