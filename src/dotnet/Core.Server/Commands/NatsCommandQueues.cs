using ActualChat.Hosting;

namespace ActualChat.Commands;

public class NatsCommandQueues(NatsCommandQueues.Options settings, IServiceProvider services) : ICommandQueues
{
    private readonly ConcurrentDictionary<QueueId, NatsCommandQueue> _queues = new ();

    public sealed record Options
    {
        public string CommonPrefix { get; init; } = "";
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

    public async Task Purge(CancellationToken cancellationToken)
    {
        foreach (var queue in _queues.Values)
            await queue.Purge(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private NatsCommandQueue Get(QueueId queueId)
        => _queues.GetOrAdd(
            queueId,
            static (queueId1, self) => queueId1.HostRole == HostRole.EventQueue
                ? new NatsEventQueue(queueId1, self, self.Services)
                : new NatsCommandQueue(queueId1, self, self.Services),
            this);
}
