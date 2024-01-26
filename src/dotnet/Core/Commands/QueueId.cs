using MemoryPack;

namespace ActualChat.Commands;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct QueueId(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] int ShardIndex,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] QueuedCommandPriority Priority = QueuedCommandPriority.Normal)
{
    public QueueId WithShardIndexMask(int shardIndexMask)
        => this with { ShardIndex = ShardIndex & shardIndexMask };

    public QueueId WithShardIndexModulo(int shardKeyModulo)
    {
        var m = ShardIndex % shardKeyModulo;
        if (m < 0)
            m += shardKeyModulo;
        return this with { ShardIndex = m };
    }
}
