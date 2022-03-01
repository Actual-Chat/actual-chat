namespace ActualChat.Audio;

public interface IAudioProcessor
{
    Task ProcessAudio(
        AudioRecord record,
        IAsyncEnumerable<AudioFrame> recordingStream,
        CancellationToken cancellationToken);
}
