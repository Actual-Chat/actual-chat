namespace ActualChat.Commands;

public interface ICommandQueueProvider
{
    ICommandQueue Get(QueueRef queueRef);
}
