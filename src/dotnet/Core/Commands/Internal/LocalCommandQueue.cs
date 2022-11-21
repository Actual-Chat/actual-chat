namespace ActualChat.Commands.Internal;

public class LocalCommandQueue : ICommandQueue
{
    private readonly ConcurrentLruCache<ICommand, ICommand> _duplicateCache = new (128);

    public Channel<QueuedCommand> Commands { get; }

    public LocalCommandQueue()
        => Commands = Channel.CreateBounded<QueuedCommand>(
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
}
