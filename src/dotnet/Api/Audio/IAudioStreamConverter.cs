namespace ActualChat.Audio;

/// <summary>
/// Converts between byte streams and <see cref="AudioSource"/>.
/// </summary>
public interface IAudioStreamConverter
{
    Task<AudioSource> FromByteStream(
        IAsyncEnumerable<byte[]> byteStream,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<(byte[] Buffer, AudioFrame? LastFrame)> ToByteFrameStream(
        AudioSource source,
        CancellationToken cancellationToken = default);
}
