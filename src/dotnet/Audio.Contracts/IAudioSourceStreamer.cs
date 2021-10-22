namespace ActualChat.Audio;

public interface IAudioSourceStreamer
{
    Task<AudioSource> GetAudioSource(StreamId streamId, TimeSpan offset, CancellationToken cancellationToken);
}
