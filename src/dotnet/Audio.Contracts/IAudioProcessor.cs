using ActualChat.Media;

namespace ActualChat.Audio;

public interface IAudioProcessor
{
    Task ProcessAudio(
        AudioRecord record,
        IAsyncEnumerable<RecordingPart> recordingStream,
        CancellationToken cancellationToken);
}
