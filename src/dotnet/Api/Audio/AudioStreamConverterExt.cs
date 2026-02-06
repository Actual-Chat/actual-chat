namespace ActualChat.Audio;

/// <summary>
/// Extension methods for <see cref="IAudioStreamConverter"/>.
/// </summary>
public static class AudioStreamConverterExt
{
    public static IAsyncEnumerable<byte[]> ToByteStream(
        this IAudioStreamConverter converter,
        AudioSource source,
        CancellationToken cancellationToken = default)
        => converter.ToByteFrameStream(source, cancellationToken).Select(x => x.Buffer);
}
