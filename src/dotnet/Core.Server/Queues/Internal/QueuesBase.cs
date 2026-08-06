
namespace ActualChat.Queues.Internal;

#pragma warning disable CA2214

public abstract record QueueSettings
{
    public int ConcurrencyLevel { get; init; } = HardwareInfo.GetProcessorCountFactor(8);
    public TimeSpan GracefulStopDelay { get; init; } = TimeSpan.FromSeconds(5);
    public MomentClock? Clock { get; init; }
}

public abstract class QueuesBase<TSettings> : WorkerBase, IQueues
    where TSettings : QueueSettings
{
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public HostInfo HostInfo { get; }
    public MomentClock Clock { get; }

    public TSettings Settings { get; }
    public IReadOnlyDictionary<QueueRef, IQueueProcessor> Processors { get; protected set; } = null!;

    protected QueuesBase(TSettings settings, IServiceProvider services, bool createProcessors = true)
        : base(services.HostLifetimeIfExist().CreateStopTokenSource())
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());

        HostInfo = services.HostInfo();
        Clock = settings.Clock ?? services.Clocks().SystemClock;
        if (createProcessors)
            CreateProcessors();
    }

    public override string ToString()
        => $"{GetType().GetName()}(Processors: [{Processors.Keys.Select(x => x.Format()).ToDelimitedString()}])";

    public IQueueSender GetSender(QueueRef queueRef)
    {
        queueRef.RequireValid();
        return Processors[queueRef];
    }

    public abstract Task Purge(CancellationToken cancellationToken = default);

    // Protected methods

    protected abstract IQueueProcessor CreateProcessor(QueueRef queueRef);

    protected void CreateProcessors()
    {
        var queueRefs = ShardScheme.ById.Values
            .Where(x => x.IsValid && x.Flags.HasFlag(ShardSchemeFlags.Queue))
            .Select(x => new QueueRef(x))
            .Distinct();

        Processors = queueRefs.ToDictionary(x => x, CreateProcessor);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var queueProcessors = Processors
            .Where(kv => HostInfo.HasRole(kv.Key.ShardScheme.HostRole))
            .ToArray();
        var sQueueProcessors = queueProcessors
            .Select(p => p.Key.ShardScheme.Name)
            .ToDelimitedString()
            .NullIfEmpty()
            ?? "(none)";
        Log.LogInformation("Starting queue processors: {QueueProcessors}", sQueueProcessors);
        foreach (var (_, processor) in queueProcessors)
            processor.Start();

        // Waiting for the stop signal
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);

        Log.LogInformation("Stopping queue processors...");
        var disposeTasks = queueProcessors.Select(kv => kv.Value.DisposeSilentlyAsync().AsTask()).ToArray();
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
    }
}
