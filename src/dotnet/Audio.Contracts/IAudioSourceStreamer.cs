namespace ActualChat.Audio;

public interface IAudioSourceStreamer
{
    Task<AudioSource> GetAudioSource(StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken);
}
