using ActualChat.Concurrency;
using ActualChat.Hosting;

namespace ActualChat.Commands.Internal;

public class LocalCommandQueueScheduler : WorkerBase, ICommandQueueScheduler
{
    private const int ShardIndex = 0;

    private static bool DebugMode => Constants.DebugMode.CommandQueue;

    private long _lastCommandTicks = 0;
    private ILogger? DebugLog => DebugMode ? Log : null;
    private ILogger Log { get; }
    private IServiceProvider Services { get; }
    private ICommandQueues Queues { get; }
    private ICommander Commander { get; }
    private RecentlySeenMap<Ulid, Unit> KnownCommands { get; }

    public LocalCommandQueues.Options Settings { get; }
    public IMomentClock Clock { get; }

    public LocalCommandQueueScheduler(LocalCommandQueues.Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Clock = services.Clocks().SystemClock;
        Log = services.LogFor(GetType());

        Queues = services.GetRequiredService<LocalCommandQueues>();
        Commander = services.GetRequiredService<ICommander>();
        KnownCommands = new RecentlySeenMap<Ulid, Unit>(
            Settings.MaxKnownCommandCount,
            Settings.MaxKnownCommandAge);
    }

    public async Task ProcessAlreadyQueued(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var clock = Services.Clocks().SystemClock;
        while (true) {
            await clock.Delay(timeout, cancellationToken).ConfigureAwait(false);

            var lastCommandTicks = Interlocked.Read(ref _lastCommandTicks);
            var currentTicks = clock.UtcNow.Ticks;
            if (currentTicks - lastCommandTicks > timeout.Ticks)
                break;
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => ProcessQueue(new QueueId(HostRole.AnyServer, ShardIndex), cancellationToken);

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
        finally {
            var commandCompletionTicks = Clock.UtcNow.Ticks;
            InterlockedExt.ExchangeIfGreaterThan(ref _lastCommandTicks, commandCompletionTicks);
        }
    }
}
