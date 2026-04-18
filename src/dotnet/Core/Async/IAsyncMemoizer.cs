namespace ActualChat;

public interface IAsyncMemoizer : IWorker
{
    int Capacity { get; }
    bool IsUnbounded { get; }

    int BufferedCount { get; }
    Exception? Completion { get; }
    bool IsCompleted { get; }
}

public interface IAsyncMemoizer<T> : IAsyncMemoizer
{
    IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default);
    IAsyncEnumerable<T> Replay(int tailSize, CancellationToken cancellationToken = default);
    Task AddReplayTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default);
    Task AddReplayTarget(ChannelWriter<T> channel, int tailSize, CancellationToken cancellationToken = default);
}
