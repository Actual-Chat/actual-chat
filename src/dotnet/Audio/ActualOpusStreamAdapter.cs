namespace ActualChat.Audio;

public class ActualOpusStreamAdapter : IAudioStreamAdapter
{
    private readonly ILogger _log;

    public ActualOpusStreamAdapter(ILogger log)
        => _log = log;

    public Task<AudioSource> Read(IAsyncEnumerable<byte[]> byteStream, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public IAsyncEnumerable<byte[]> Write(AudioSource source, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
