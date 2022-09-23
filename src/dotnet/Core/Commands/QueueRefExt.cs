namespace ActualChat.Commands;

public static class QueueRefExt
{
    public static QueueRef ShardBy(this QueueRef queueRef, string shardKey)
        => new ();

    public static QueueRef WithPriority(this QueueRef queueRef, CommandPriority priority)
        => new ();
}
