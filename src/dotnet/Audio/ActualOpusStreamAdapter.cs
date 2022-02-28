namespace ActualChat.Audio;

public class ActualOpusStreamAdapter : IAudioStreamAdapter
{
    public Task<AudioSource> Read(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public IAsyncEnumerable<byte[]> Write(AudioSource source, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
