using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Commands;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct QueueId(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] HostRole HostRole,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] int ShardIndex);
