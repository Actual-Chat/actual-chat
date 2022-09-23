namespace ActualChat.Commands;

public class LocalCommandScheduler : WorkerBase
{
    private LocalCommandQueue LocalCommandQueue { get; }
    private ICommander Commander { get; }

    public LocalCommandScheduler(LocalCommandQueue localCommandQueue, ICommander commander)
    {
        LocalCommandQueue = localCommandQueue;
        Commander = commander;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var commands = LocalCommandQueue.Read(cancellationToken);
        await foreach (var command in commands.ConfigureAwait(false))
            _ = Commander.Start(command, true, cancellationToken);
    }
}
