namespace ActualChat;

public interface IAsyncMemoizer<out T>
{
    Task WriteTask { get; }
    IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default);
}
