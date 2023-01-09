namespace ActualChat.Commands;

public class CommandQueueScheduler : WorkerBase
{
    public sealed class Options
    {
        public ImmutableHashSet<(Symbol QueueName, Symbol ShardKey)> Queues { get; set; } =
            ImmutableHashSet<(Symbol QueueName, Symbol ShardKey)>.Empty;
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
        public int MaxTryCount { get; set; } = 2;

        public void AddQueue(Symbol queueName, Symbol shardKey = default)
            => Queues = Queues.Add((queueName, shardKey));
    }

    private ILogger Log { get; }
    private ICommandQueues CommandQueues { get; }
    private ICommander Commander { get; }

    public Options Settings { get; }

    public CommandQueueScheduler(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Log = services.LogFor(GetType());
        CommandQueues = services.GetRequiredService<ICommandQueues>();
        Commander = services.GetRequiredService<ICommander>();
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
        var queueReader = CommandQueues.GetReader(queueName, shardKey);
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Settings.Concurrency,
            CancellationToken = cancellationToken,
        };
        var commands = queueReader.Read(cancellationToken);
        return Parallel.ForEachAsync(commands, parallelOptions, (c, ct) => ProcessCommand(queueReader, c, ct));
    }

    private async ValueTask ProcessCommand(
        ICommandQueueReader queueReader,
        QueuedCommand command,
        CancellationToken cancellationToken)
    {
        try {
            await Commander.Call(command.Command, true, cancellationToken).ConfigureAwait(false);
            await queueReader.MarkCompleted(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Error processing queued command: {Command}", command);
            var mustRetry = command.TryIndex + 1 >= Settings.MaxTryCount;
            await queueReader.MarkFailed(command, mustRetry, e, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw;
        }
    }
}
