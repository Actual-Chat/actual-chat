namespace ActualChat.Commands;

public class NatsCommandQueues(NatsCommandQueues.Options settings, IServiceProvider services) : ICommandQueues
{
    private readonly ConcurrentDictionary<QueueId, NatsCommandQueue> _queues = new ();

    public sealed record Options
    {
        public int MaxQueueSize { get; init; } = Constants.Queues.SharedCommandQueueDefaultSize;
        public int ReplicaCount { get; init; } = 0;
        public int MaxTryCount { get; set; } = 2;
    }

    public IServiceProvider Services { get; } = services;
    public IMomentClock Clock { get; } = services.Clocks().SystemClock;
    public Options Settings { get; } = settings;

    public ICommandQueue this[QueueId queueId] => Get(queueId);

    public ICommandQueueBackend GetBackend(QueueId queueId)
        => Get(queueId);

    // Private methods

    private NatsCommandQueue Get(QueueId queueId)
        => _queues.GetOrAdd(
            queueId,
            static (queueId2, self) => new NatsCommandQueue(queueId2, self, self.Services),
            this);
}
