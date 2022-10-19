namespace ActualChat.Commands;

public interface ICommandQueues
{
    ICommandQueue Get(QueueRef queueRef);
}
