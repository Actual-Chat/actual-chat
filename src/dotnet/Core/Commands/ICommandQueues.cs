namespace ActualChat.Commands;

public interface ICommandQueues
{
    ICommandQueue this[QueueRef queueRef] { get; }
    ICommandQueueReader GetReader(Symbol queueName, Symbol shardKey);
}
