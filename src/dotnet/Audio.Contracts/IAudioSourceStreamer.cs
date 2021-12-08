namespace ActualChat.Audio;

public interface IAudioSourceStreamer
{
    Task<AudioSource> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken);
}
