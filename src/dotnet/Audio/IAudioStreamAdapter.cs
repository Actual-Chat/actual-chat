namespace ActualChat.Audio;

public interface IAudioStreamAdapter
{
    Task<AudioSource> Read(Stream stream, CancellationToken cancellationToken);
    Task Write(AudioSource source, Stream target, CancellationToken cancellationToken);
}
