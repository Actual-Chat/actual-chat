namespace ActualChat.Commands;

public class LocalCommandQueueProvider : ICommandQueueProvider
{
    private LocalCommandQueue CommandQueue { get; }

    public LocalCommandQueueProvider(LocalCommandQueue commandQueue)
        => CommandQueue = commandQueue;

    public ICommandQueue Get(QueueRef queueRef)
        => CommandQueue;
}
