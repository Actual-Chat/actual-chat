namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueues : ICommandQueues
{
    public sealed record Options
    {
        public int MaxQueueSize { get; init; } = Constants.Queues.LocalCommandQueueDefaultSize;
    }

    // NOTE(AY): We ignore ShardKey here for now, coz there are no shards
    private readonly ConcurrentDictionary<Symbol, LocalCommandQueue> _queues;

    public Options Settings { get; }
    public IServiceProvider Services { get; }
    public IMomentClock Clock { get; }

    public ICommandQueue this[QueueRef queueRef] => GetBackend(queueRef.Name);

    public LocalCommandQueues(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Clock = QueuedCommand.Clock;
        _queues = new ConcurrentDictionary<Symbol, LocalCommandQueue>();
    }

    public ICommandQueueBackend GetBackend(Symbol queueName, Symbol shardKey)
        => GetBackend(queueName);

    // Private methods

    private LocalCommandQueue GetBackend(Symbol queueName)
        => _queues.GetOrAdd(queueName, static (name, self) => new LocalCommandQueue(name, self), this);
}
