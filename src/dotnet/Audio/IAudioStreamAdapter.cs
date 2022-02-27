namespace ActualChat.Audio;

public interface IAudioStreamAdapter
{
    Task<AudioSource> Read(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken);
    IAsyncEnumerable<byte[]> Write(AudioSource source, CancellationToken cancellationToken);
}
