namespace ActualChat.Audio;

public interface IAudioProcessor
{
    Task ProcessAudio(
        AudioRecord record,
        int preSkipFrames,
        IAsyncEnumerable<AudioFrame> recordingStream,
        CancellationToken cancellationToken);
}
