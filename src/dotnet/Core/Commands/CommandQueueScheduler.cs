namespace ActualChat.Commands;

public class CommandQueueScheduler : WorkerBase
{
    public sealed class Options
    {
        public ImmutableHashSet<(Symbol QueueName, Symbol ShardKey)> Queues { get; set; } =
            ImmutableHashSet<(Symbol QueueName, Symbol ShardKey)>.Empty;
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
        public int MaxTryCount { get; set; } = 2;
        public int MaxKnownCommandCount { get; init; } = 10_000;
        public TimeSpan MaxKnownCommandAge { get; init; } = TimeSpan.FromHours(1);

        public void AddQueue(Symbol queueName, Symbol shardKey = default)
            => Queues = Queues.Add((queueName, shardKey));
    }

    protected ILogger Log { get; }
    protected IServiceProvider Services { get; }
    protected ICommandQueues Queues { get; }
    protected ICommander Commander { get; }
    protected RecentlySeenMap<Symbol, Unit> KnownCommands { get; }

    public Options Settings { get; }

    public CommandQueueScheduler(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());

        Queues = services.GetRequiredService<ICommandQueues>();
        Commander = services.GetRequiredService<ICommander>();
        KnownCommands = new RecentlySeenMap<Symbol, Unit>(
            Settings.MaxKnownCommandCount,
            Settings.MaxKnownCommandAge);
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var tasks = Settings.Queues
            .Select(x => ProcessQueue(x.QueueName, x.ShardKey, cancellationToken))
            .ToList();
        return Task.WhenAll(tasks);
    }

    private Task ProcessQueue(Symbol queueName, Symbol shardKey, CancellationToken cancellationToken)
    {
        var queueBackend = Queues.GetBackend(queueName, shardKey);
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Settings.Concurrency,
            CancellationToken = cancellationToken,
        };
        var commands = queueBackend.Read(cancellationToken);
        return Parallel.ForEachAsync(commands, parallelOptions, (c, ct) => RunCommand(queueBackend, c, ct));
    }

    private async ValueTask RunCommand(
        ICommandQueueBackend queueBackend,
        QueuedCommand command,
        CancellationToken cancellationToken)
    {
        lock (Lock) {
            // We de-duplicate commands not only in QueuedCommand.New, but here as well:
            // - Some commands are sent to multiple queues
            // - But all of them have to be processed just once.
            // Note that here we rely on QueuedCommand.Id instead of its Command value,
            // coz Command instance is definitely non-unique here due to deserialization.
            if (!KnownCommands.TryAdd(command.Id))
                return;
        }

        Log.LogInformation("Running queued command: {Command}", command);
        try {
            await Commander.Call(command.Command, true, cancellationToken).ConfigureAwait(false);
            await queueBackend.MarkCompleted(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Running queued command failed: {Command}", command);
            var mustRetry = command.TryIndex + 1 < Settings.MaxTryCount;
            await queueBackend.MarkFailed(command, mustRetry, e, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw;
        }
    }
}
