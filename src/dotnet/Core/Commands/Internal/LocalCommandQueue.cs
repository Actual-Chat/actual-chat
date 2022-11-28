using Stl.Collections.Slim;

namespace ActualChat.Commands.Internal;

public class LocalCommandQueue : ICommandQueue
{
    private readonly ConcurrentLruCache<ICommand, ICommand> _duplicateCache =
        new (128, 0, ReferenceEqualityComparer<ICommand>.Instance);

    private readonly ConcurrentLruCache<ICommand, IQueuedCommand> _completedCache =
        new (16, 0, ReferenceEqualityComparer<ICommand>.Instance);

    private volatile int _completedCommandCount;

    public Channel<IQueuedCommand> Commands { get; }
    public int CompletedCommandCount => _completedCommandCount;

    public LocalCommandQueue()
        => Commands = Channel.CreateBounded<IQueuedCommand>(
            new BoundedChannelOptions(128) {
                FullMode = BoundedChannelFullMode.Wait,
            });

    public Task Enqueue(ICommand command, CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        // skip duplicates
        if (!_duplicateCache.TryAdd(command, command))
            return Task.CompletedTask;

        var queuedCommand = new QueuedCommand(Ulid.NewUlid().ToString(), command);
        return Commands.Writer.WriteAsync(queuedCommand, cancellationToken).AsTask();
    }

    public Task Enqueue(IQueuedCommand queuedCommand, CancellationToken cancellationToken = default)
    {
        if (queuedCommand == null) throw new ArgumentNullException(nameof(queuedCommand));

        // skip duplicates
        return !_duplicateCache.TryAdd(queuedCommand.Command, queuedCommand.Command)
            ? Task.CompletedTask
            : Commands.Writer.WriteAsync(queuedCommand, cancellationToken).AsTask();
    }

    public void SetFailed(IQueuedCommand queuedCommand)
        => _completedCache.Remove(queuedCommand.Command);

    public void SetCompleted(IQueuedCommand queuedCommand)
    {
        Interlocked.Increment(ref _completedCommandCount);
        _completedCache.TryAdd(queuedCommand.Command, queuedCommand);
    }

    public bool IsCompleted(ICommand command)
        => _completedCache.TryGetValue(command, out _);
}
