using ActualChat.Hosting;

namespace ActualChat.Commands.Internal;

public class LocalCommandQueueScheduler : WorkerBase, ICommandQueueScheduler
{
    private const int ShardIndex = 0;

    private static bool DebugMode => Constants.DebugMode.CommandQueue;
    private ILogger? DebugLog => DebugMode ? Log : null;
    private ILogger Log { get; }
    private IServiceProvider Services { get; }
    private ICommandQueues Queues { get; }
    private ICommander Commander { get; }
    private RecentlySeenMap<Ulid, Unit> KnownCommands { get; }

    public LocalCommandQueues.Options Settings { get; }

    public LocalCommandQueueScheduler(LocalCommandQueues.Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());

        Queues = services.GetRequiredService<LocalCommandQueues>();
        Commander = services.GetRequiredService<ICommander>();
        KnownCommands = new RecentlySeenMap<Ulid, Unit>(
            Settings.MaxKnownCommandCount,
            Settings.MaxKnownCommandAge);
    }

    public async Task ProcessAlreadyQueued(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var queueId = new QueueId(HostRole.BackendServer, ShardIndex);
        var queueBackend = Queues.GetBackend(queueId);
        var bufferedCommands = queueBackend.Read(cancellationToken).Buffer(timeout, Services.Clocks().SystemClock, cancellationToken);
        await foreach (var commands in bufferedCommands) {
            if (commands.Count == 0)
                return;

            foreach (var command in commands)
                await RunCommand(queueBackend, command, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => ProcessQueue(new QueueId(HostRole.BackendServer, ShardIndex), cancellationToken);

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
            await queueBackend.MarkFailed(command, e, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw;
        }
    }
}
