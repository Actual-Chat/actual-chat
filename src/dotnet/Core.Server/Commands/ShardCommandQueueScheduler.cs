using ActualChat.Concurrency;
using ActualChat.Hosting;

namespace ActualChat.Commands;

public class ShardCommandQueueScheduler(HostRole hostRole, IServiceProvider services)
    : ShardWorker(services, ShardScheme.ById[hostRole], "CommandQueueScheduler"), ICommandQueueScheduler
{
    public sealed record Options
    {
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
    }

    private long _lastCommandTicks = 0;

    private HostRole HostRole { get; } = hostRole;

    private Options Settings { get; } = services.GetKeyedService<Options>(hostRole.Id.Value)
        ?? services.GetRequiredService<Options>();

    private ICommandQueues Queues { get; } = services.GetRequiredService<ICommandQueues>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();

    public async Task ProcessAlreadyQueued(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (true) {
            await Clock.Delay(timeout, cancellationToken).ConfigureAwait(false);

            var lastCommandTicks = Interlocked.Read(ref _lastCommandTicks);
            var currentTicks = Clock.UtcNow.Ticks;
            if (currentTicks - lastCommandTicks > timeout.Ticks)
                break;
        }
        await Clock.Delay(timeout, cancellationToken).ConfigureAwait(false);
    }

    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var queueId = new QueueId(HostRole, shardIndex);
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
        Log.LogDebug("Running queued command: {Command}", command);
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
