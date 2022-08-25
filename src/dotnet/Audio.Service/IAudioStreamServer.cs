namespace ActualChat.Audio;

public interface IAudioStreamServer
{
    IAsyncEnumerable<byte[]> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task Write(
        Symbol streamId,
        IAsyncEnumerable<byte[]> audioStream,
        CancellationToken cancellationToken);
}
