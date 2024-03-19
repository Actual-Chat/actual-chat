using ActualChat.Queues.Internal;

namespace ActualChat.Queues.InMemory;

public sealed class InMemoryQueues : WorkerBase, IQueues
{
    public sealed record Options
    {
        public int MaxQueueSize { get; init; } = 1024;
        public int ConcurrencyLevel { get; init; } = HardwareInfo.GetProcessorCountFactor(8);
        // public int MaxTryCount { get; init; } = 2;
        public int MaxKnownCommandCount { get; init; } = 10_000;
        public TimeSpan MaxKnownCommandAge { get; init; } = TimeSpan.FromHours(1);
        public IMomentClock? Clock { get; init; }
    }

    private readonly InMemoryQueueProcessor _processor;

    public Options Settings { get; }
    public IServiceProvider Services { get; }
    public IMomentClock Clock { get; }

    public InMemoryQueues(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Clock = settings.Clock ?? services.Clocks().SystemClock;
        _processor = new(settings, this);
    }

    public IQueueProcessor GetProcessor(QueueRef queueRef)
    {
        queueRef.RequireValid();
        return _processor;
    }

    public Task Purge(CancellationToken cancellationToken)
        => Task.CompletedTask;

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        _processor.Start();
        try {
            await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            await _processor.DisposeAsync().ConfigureAwait(false);
        }
    }
}
