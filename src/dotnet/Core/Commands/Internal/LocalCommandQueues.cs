namespace ActualChat.Commands.Internal;

public class LocalCommandQueues : ICommandQueues
{
    private LocalCommandQueue CommandQueue { get; }

    public LocalCommandQueues(LocalCommandQueue commandQueue)
        => CommandQueue = commandQueue;

    public ICommandQueue Get(QueueRef queueRef)
        => CommandQueue;
}
