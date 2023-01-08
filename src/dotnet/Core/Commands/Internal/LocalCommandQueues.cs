namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueues : ICommandQueues
{
    // NOTE(AY): We ignore ShardKey here for now, coz there are no shards
    private readonly ConcurrentDictionary<Symbol, LocalCommandQueue> _queues = new();

    private IServiceProvider Services { get; }

    public LocalCommandQueues(IServiceProvider services)
        => Services = services;

    public ICommandQueue this[QueueRef queueRef]
        => Get(queueRef.Name);

    public ICommandQueueReader GetReader(Symbol queueName, Symbol shardKey)
        => Get(queueName);

    // Private methods

    private LocalCommandQueue Get(Symbol queueName)
        => _queues.GetOrAdd(queueName, static (_, self) => self.Services.Activate<LocalCommandQueue>(), this);
}
