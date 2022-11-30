namespace ActualChat.Commands;

public interface ICommandQueues
{
    ICommandQueue Get(QueueRef queueRef);

    ICommandQueueReader Reader(string queueName, string shardIdentifier);
}
