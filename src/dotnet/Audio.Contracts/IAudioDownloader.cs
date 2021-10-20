namespace ActualChat.Audio;

public interface IAudioDownloader
{
    Task<AudioSource> GetAudioSource(Uri audioUri, TimeSpan offset, CancellationToken cancellationToken);
}
