namespace ActualChat.Commands;

public static class QueueRefExt
{
    public static QueueRef ShardBy(this QueueRef queueRef, string shardKey)
        => new (queueRef.QueueName, shardKey, queueRef.Priority);

    public static QueueRef WithPriority(this QueueRef queueRef, CommandPriority priority)
        => new (queueRef.QueueName, queueRef.ShardKey, priority);
}
