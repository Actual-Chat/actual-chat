namespace ActualChat.Audio;

public interface IAudioStreamConverter
{
    Task<AudioSource> FromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<byte[]> ToByteStream(
        AudioSource source,
        CancellationToken cancellationToken = default);
}
