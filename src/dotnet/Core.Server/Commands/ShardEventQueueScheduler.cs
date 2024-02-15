using ActualChat.Hosting;

namespace ActualChat.Commands;

public class ShardEventQueueScheduler(HostRole hostRole, IServiceProvider services)
    : ShardWorker<ShardScheme.EventQueue>(services, "EventQueueScheduler")
{
    public sealed record Options
    {
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
    }

    private HostRole HostRole { get; } = hostRole;

    private Options Settings { get; } = services.GetKeyedService<Options>(hostRole.Id.Value)
        ?? services.GetRequiredService<Options>();

    private ICommandQueues Queues { get; } = services.GetRequiredService<ICommandQueues>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();

    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var queueId = new QueueId(HostRole.EventQueue, shardIndex);
        var queueBackend = (IEventQueueBackend)Queues.GetBackend(queueId);
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Settings.Concurrency,
            CancellationToken = cancellationToken,
        };
        var events = queueBackend.Read(HostRole, cancellationToken);
        return Parallel.ForEachAsync(events, parallelOptions, (c, ct) => HandleEvent(queueBackend, c, ct));
    }

    private async ValueTask HandleEvent(
        IEventQueueBackend queueBackend,
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
    }
}
