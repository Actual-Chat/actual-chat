namespace ActualChat.Commands.Internal;

public class LocalCommandScheduler : WorkerBase
{
    private readonly string _queueName;
    private readonly int _degreeOfParallelism;
    private readonly Action<string> _dispose;

    private ICommandQueues CommandQueues { get; }
    private ICommander Commander { get; }
    private EventCommander EventCommander { get; }
    private ILogger<LocalCommandScheduler> Log { get; }

    public LocalCommandScheduler(
        string queueName,
        int degreeOfParallelism,
        IServiceProvider serviceProvider,
        Action<string> dispose)
    {
        _queueName = queueName;
        _degreeOfParallelism = degreeOfParallelism;
        _dispose = dispose;
        CommandQueues = serviceProvider.GetRequiredService<ICommandQueues>();
        Commander = serviceProvider.GetRequiredService<ICommander>();
        EventCommander = serviceProvider.GetRequiredService<EventCommander>();
        Log = serviceProvider.GetRequiredService<ILogger<LocalCommandScheduler>>();
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var queueReader = CommandQueues.Reader(_queueName, "");
        var queuedCommands = queueReader.Read(cancellationToken);
        await Parallel.ForEachAsync(
            queuedCommands,
            new ParallelOptions {
                MaxDegreeOfParallelism = _degreeOfParallelism,
                CancellationToken = cancellationToken,
            },
            async (queuedCommand, cancellationToken1) => {
                try {
                    if (queuedCommand.Command is IEvent)
                        await EventCommander.Run(queuedCommand.Command, true, cancellationToken1).ConfigureAwait(false);
                    else
                        await Commander.Run(queuedCommand.Command, true, cancellationToken1).ConfigureAwait(false);
                    await queueReader.Ack(queuedCommand, cancellationToken1).ConfigureAwait(false);
                }
                catch (OperationCanceledException e)
                {
                    await queueReader.NAck(queuedCommand, false, e, default).ConfigureAwait(false);

                    if (cancellationToken1.IsCancellationRequested)
                        throw;
                }
                catch (Exception e) {
                    Log.LogError(e, "Error processing queued command");
                    await queueReader.NAck(queuedCommand, true, e, cancellationToken1).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
    }

    protected override Task DisposeAsyncCore()
    {
        _dispose(_queueName);
        return base.DisposeAsyncCore();
    }
}

