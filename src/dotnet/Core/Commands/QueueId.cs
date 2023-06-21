using MemoryPack;

namespace ActualChat.Commands;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct QueueId(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] int SharedKey,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] QueuedCommandPriority Priority = QueuedCommandPriority.Normal)
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
