using MemoryPack;

namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct Expiring<T>(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] T Value,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] Moment ExpiresAt = default);
