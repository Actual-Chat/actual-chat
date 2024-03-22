using ActualChat.Queues.Internal;

namespace ActualChat.Queues.InMemory;

public sealed class InMemoryQueues : QueuesBase<InMemoryQueues.Options, InMemoryQueueProcessor>
{
    public sealed record Options : QueueSettings
    {
        public int MaxQueueSize { get; init; } = 1024;
        public int MaxKnownCommandCount { get; init; } = 10_000;
        public TimeSpan MaxKnownCommandAge { get; init; } = TimeSpan.FromHours(1);
    }

    private readonly InMemoryQueueProcessor _processor;

    public InMemoryQueues(Options settings, IServiceProvider services)
        : base(settings, services, false)
    {
        _processor = new(settings, this);
        Processors = CreateProcessors();
    }

    public override Task Purge(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // Protected methods

    protected override InMemoryQueueProcessor CreateProcessor(QueueRef queueRef)
        => _processor; // There is just a single processor
}
