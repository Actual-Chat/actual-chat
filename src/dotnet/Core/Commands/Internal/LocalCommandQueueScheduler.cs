namespace ActualChat.Commands.Internal;

public class LocalCommandQueueScheduler : WorkerBase
{
    private const int ShardKey = 0;

    private static bool DebugMode => Constants.DebugMode.CommandQueue;
    private ILogger? DebugLog => DebugMode ? Log : null;
    private ILogger Log { get; }
    private IServiceProvider Services { get; }
    private ICommandQueues Queues { get; }
    private ICommander Commander { get; }
    private RecentlySeenMap<Symbol, Unit> KnownCommands { get; }

    public LocalCommandQueues.Options Settings { get; }

    public LocalCommandQueueScheduler(LocalCommandQueues.Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());

        Queues = services.GetRequiredService<LocalCommandQueues>();
        Commander = services.GetRequiredService<ICommander>();
        KnownCommands = new RecentlySeenMap<Symbol, Unit>(
            Settings.MaxKnownCommandCount,
            Settings.MaxKnownCommandAge);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var priorities = new [] {
            QueuedCommandPriority.Low,
            QueuedCommandPriority.Normal,
            QueuedCommandPriority.High,
            QueuedCommandPriority.Critical,
        };
        var tasks = (
            from priority in priorities
            let queueId = new QueueId(ShardKey, priority)
            select ProcessQueue(queueId, cancellationToken)
            ).ToList();
        return Task.WhenAll(tasks);
    }

    private Task ProcessQueue(QueueId queueId, CancellationToken cancellationToken)
    {
        var queueBackend = Queues.GetBackend(queueId);
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
            // We de-duplicate commands here to make sure we process them just once.
            // Note that here we rely on QueuedCommand.Id instead of its Command value,
            // coz Command instance is definitely non-unique here due to deserialization.
            if (!KnownCommands.TryAdd(command.Id))
                return;
        }

        DebugLog?.LogDebug("Running queued command: {Command}", command);
        try {
            await Commander.Call(command.UntypedCommand, true, cancellationToken).ConfigureAwait(false);
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
