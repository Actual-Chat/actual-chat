namespace ActualChat.Commands;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly record struct QueueId(
    [property: DataMember(Order = 0)] int SharedKey,
    [property: DataMember(Order = 1)] QueuedCommandPriority Priority = QueuedCommandPriority.Normal)
{
    public QueueId WithShardKeyMask(int shardKeyMask)
        => this with { SharedKey = SharedKey & shardKeyMask };

    public QueueId WithShardKeyModulo(int shardKeyModulo)
    {
        var m = SharedKey % shardKeyModulo;
        if (m < 0)
            m += shardKeyModulo;
        return this with { SharedKey = m };
    }
}
