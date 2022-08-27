namespace ActualChat.Audio;

public interface IAudioStreamServer
{
    Task<Option<IAsyncEnumerable<byte[]>>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task<Task> Write(
        Symbol streamId,
        IAsyncEnumerable<byte[]> audioStream,
        CancellationToken cancellationToken);
}
