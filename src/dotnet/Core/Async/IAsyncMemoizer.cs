namespace ActualChat;

public interface IAsyncMemoizer<out T>
{
    IAsyncEnumerable<T> Replay(CancellationToken cancellationToken);
    Task WriteTask { get; }
}
