namespace ActualChat.Commands.Internal;

public class LocalCommandScheduler : WorkerBase
{
    private readonly string _queueName;
    private readonly int _degreeOfParallelism;

    private ICommandQueues CommandQueues { get; }
    private ICommander Commander { get; }
    private ILogger<LocalCommandScheduler> Log { get; }

    public LocalCommandScheduler(
        string queueName,
        int degreeOfParallelism,
        IServiceProvider serviceProvider)
    {
        _queueName = queueName;
        _degreeOfParallelism = degreeOfParallelism;
        CommandQueues = serviceProvider.GetRequiredService<ICommandQueues>();
        Commander = serviceProvider.GetRequiredService<ICommander>();
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
                    await Commander.Run(queuedCommand.Command, true, cancellationToken1).ConfigureAwait(false);
                    await queueReader.Ack(queuedCommand, cancellationToken1).ConfigureAwait(false);
                }
                catch (OperationCanceledException e)
                {
                    await queueReader.NAck(queuedCommand, true, e, cancellationToken1).ConfigureAwait(false);
                    if (cancellationToken1.IsCancellationRequested)
                        throw;
                }
                catch (Exception e) {
                    Log.LogError(e, "Error processing queued command");
                    await queueReader.NAck(queuedCommand, true, e, cancellationToken1).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
    }
}

