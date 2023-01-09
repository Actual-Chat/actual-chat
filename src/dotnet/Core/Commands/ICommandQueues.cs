namespace ActualChat.Commands;

public interface ICommandQueues : IHasServices
{
    IMomentClock Clock { get; }
    ICommandQueue this[QueueRef queueRef] { get; }

    ICommandQueueBackend GetBackend(Symbol queueName, Symbol shardKey);
}
