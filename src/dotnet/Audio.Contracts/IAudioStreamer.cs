namespace ActualChat.Audio;

public interface IAudioStreamer
{
    Task<AudioSource> GetAudio(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken);
}
