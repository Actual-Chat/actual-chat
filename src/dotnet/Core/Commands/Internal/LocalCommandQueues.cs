namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueues(LocalCommandQueues.Options settings, IServiceProvider services) : ICommandQueues
{
    public sealed record Options
    {
        public int MaxQueueSize { get; init; } = Constants.Queues.LocalCommandQueueDefaultSize;
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
        public int MaxTryCount { get; set; } = 2;
        public int MaxKnownCommandCount { get; init; } = 10_000;
        public TimeSpan MaxKnownCommandAge { get; init; } = TimeSpan.FromHours(1);
    }

    private readonly ConcurrentDictionary<QueueId, LocalCommandQueue> _queues = new ();

    public Options Settings { get; } = settings;
    public IServiceProvider Services { get; } = services;
    public IMomentClock Clock { get; } = QueuedCommand.Clock;

    public ICommandQueue this[QueueId queueId] => Get(queueId);

    public ICommandQueueBackend GetBackend(QueueId queueId)
        => Get(queueId);

    // Private methods

    private LocalCommandQueue Get(QueueId queueId)
        => _queues.GetOrAdd(
            queueId,
            static (queueId, self) => new LocalCommandQueue(queueId, self), this);
}
