namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueues : ICommandQueues
{
    public sealed record Options
    {
        public int ShardKeyMask { get; set; } = 0;
        public int MaxQueueSize { get; init; } = Constants.Queues.LocalCommandQueueDefaultSize;
    }

    // NOTE(AY): We ignore ShardKey here for now, coz there are no shards
    private readonly ConcurrentDictionary<QueueId, LocalCommandQueue> _queues;

    public Options Settings { get; }
    public IServiceProvider Services { get; }
    public IMomentClock Clock { get; }

    public ICommandQueue this[QueueId queueId] => Get(queueId);

    public LocalCommandQueues(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Clock = QueuedCommand.Clock;
        _queues = new ConcurrentDictionary<QueueId, LocalCommandQueue>();
    }

    public ICommandQueueBackend GetBackend(QueueId queueId)
        => Get(queueId);

    // Private methods

    private LocalCommandQueue Get(QueueId queueId)
        => _queues.GetOrAdd(
            queueId.WithShardKeyMask(Settings.ShardKeyMask),
            static (queueId, self) => new LocalCommandQueue(queueId, self), this);
}
