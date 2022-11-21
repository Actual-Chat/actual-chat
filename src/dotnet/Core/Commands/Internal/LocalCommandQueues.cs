namespace ActualChat.Commands.Internal;

public class LocalCommandQueues : ICommandQueues
{
    private readonly ConcurrentDictionary<string, LocalCommandQueue> _queues = new (StringComparer.Ordinal);

    public LocalCommandQueues()
    { }

    public ICommandQueue Get(QueueRef queueRef)
        => _queues.GetOrAdd(queueRef.Name, _ => new LocalCommandQueue());

    public ICommandQueueReader Reader(string queueName, string shardIdentifier)
        => new LocalCommandQueueReader(_queues.GetOrAdd(queueName, _ => new LocalCommandQueue()));
}
