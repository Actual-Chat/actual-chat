namespace ActualChat.Audio;

public interface IAudioProcessor
{
    Task ProcessAudio(
        AudioRecord record,
        IAsyncEnumerable<byte[]> recordingStream,
        CancellationToken cancellationToken);
}
