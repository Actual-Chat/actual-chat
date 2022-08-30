namespace ActualChat.Audio;

public interface IAudioStreamServer
{
    Task<IAsyncEnumerable<byte[]>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task Write(
        Symbol streamId,
        IAsyncEnumerable<byte[]> stream,
        CancellationToken cancellationToken);
}
