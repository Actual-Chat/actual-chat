namespace ActualChat.Audio;

public interface IAudioSourceStreamer
{
    Task<AudioSource> GetAudio(StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken);
}
