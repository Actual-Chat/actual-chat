using ActualChat.Hosting;

namespace ActualChat.Queues.Internal;

#pragma warning disable CA2214

public abstract record QueueSettings
{
    public string InstanceName { get; init; } = "";
    public int ConcurrencyLevel { get; init; } = HardwareInfo.GetProcessorCountFactor(8);
    public IMomentClock? Clock { get; init; }
}

public abstract class QueuesBase<TSettings, TProcessor> : WorkerBase, IQueues
    where TSettings : QueueSettings
    where TProcessor : IQueueProcessor

{
    protected readonly ConcurrentDictionary<QueueRef, TProcessor> ProcessorsAndSenders = new();

    public IServiceProvider Services { get; }
    public HostInfo HostInfo { get; }
    public IMomentClock Clock { get; }

    public TSettings Settings { get; }
    public IReadOnlyDictionary<QueueRef, IQueueProcessor> Processors { get; protected init; } = null!;

    protected QueuesBase(TSettings settings, IServiceProvider services, bool initProcessors = true)
    {
        Settings = settings;
        Services = services;
        HostInfo = services.HostInfo();
        Clock = settings.Clock ?? services.Clocks().SystemClock;
        if (initProcessors)
            // ReSharper disable once VirtualMemberCallInConstructor
            Processors = CreateProcessors();
    }

    public override string ToString()
        => $"{GetType().GetName()}(Processors: [{Processors.Keys.Select(x => x.Format()).ToDelimitedString()}])";

    public virtual IQueueSender GetSender(QueueRef queueRef)
    {
        queueRef.RequireValid();
        return GetProcessor(queueRef);
    }

    public abstract Task Purge(CancellationToken cancellationToken = default);

    // Protected methods

    protected abstract TProcessor CreateProcessor(QueueRef queueRef);

    protected TProcessor GetProcessor(QueueRef queueRef)
        => ProcessorsAndSenders.GetOrAdd(queueRef, static (queueRef1, self) => self.CreateProcessor(queueRef1), this);

    protected virtual IReadOnlyDictionary<QueueRef, IQueueProcessor> CreateProcessors()
    {
        var result = new HashSet<QueueRef>();
        foreach (var shardScheme in ShardScheme.ById.Values) {
            if (!(shardScheme.IsValid && HostInfo.HasRole(shardScheme.HostRole)))
                continue;

            result.Add(shardScheme);
        }
        return result.ToDictionary(x => x, x => (IQueueProcessor)GetProcessor(x));
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        foreach (var processor in Processors.Values)
            processor.Start();
        try {
            await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            var disposeTasks = Processors.Values.Select(p => p.DisposeSilentlyAsync().AsTask()).ToArray();
            await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        }
    }
}
