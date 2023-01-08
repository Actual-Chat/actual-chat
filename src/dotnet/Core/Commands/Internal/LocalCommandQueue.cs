using Stl.Comparison;

namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueue : ICommandQueue, ICommandQueueReader
{
    public sealed record Options
    {
        public int Capacity { get; init; } = 1000;
        public int MaxKnownCommandCount { get; init; } = 10_000;
        public TimeSpan MaxKnownCommandAge { get; init; } = TimeSpan.FromHours(1);
        public IMomentClock? Clock { get; init; }
    }

    private readonly RecentlySeenSet<Ref<ICommand>> _knownCommands;
    private readonly Channel<QueuedCommand> _commands;
    private volatile int _successCount;
    private volatile int _failureCount;
    private volatile int _retryCount;

    public Options Settings { get; }
    public int SuccessCount => _successCount;
    public int FailureCount => _failureCount;
    public int RetryCount => _retryCount;

    public LocalCommandQueue(Options settings, IServiceProvider services)
    {
        Settings = settings;
        var clock = settings.Clock ?? services.Clocks().CoarseCpuClock;
        _knownCommands = new RecentlySeenSet<Ref<ICommand>>(
            settings.MaxKnownCommandCount, settings.MaxKnownCommandAge, clock);
        _commands = Channel.CreateBounded<QueuedCommand>(
            new BoundedChannelOptions(settings.Capacity) {
                FullMode = BoundedChannelFullMode.Wait,
            });
    }

    public Task Enqueue(ICommand command, CancellationToken cancellationToken = default)
    {
        lock (_knownCommands) {
            if (!_knownCommands.TryAdd(Ref.New(command)))
                return Task.CompletedTask;
        }

        return _commands.Writer.WriteAsync(new QueuedCommand(NewId(), command), cancellationToken).AsTask();
    }

    public IAsyncEnumerable<QueuedCommand> Read(CancellationToken cancellationToken)
        => _commands.Reader.ReadAllAsync(cancellationToken);

    public ValueTask MarkCompleted(QueuedCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _successCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailed(QueuedCommand command, bool mustRetry, Exception? exception, CancellationToken cancellationToken)
    {
        if (!mustRetry) {
            Interlocked.Increment(ref _failureCount);
            return ValueTask.CompletedTask;
        }

        Interlocked.Increment(ref _retryCount);
        var newTryIndex = command.TryIndex + 1;
        var newCommand = new QueuedCommand(
            $"{command.Id.Value}-retry-{newTryIndex.ToString(CultureInfo.InvariantCulture)}",
            command.Command,
            newTryIndex);
        return _commands.Writer.WriteAsync(newCommand, cancellationToken);
    }

    // Private methods

    private static string NewId()
        => Ulid.NewUlid().ToString();
}
