namespace ActualChat.Audio;

public interface IAudioStreamer
{
    Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
}
