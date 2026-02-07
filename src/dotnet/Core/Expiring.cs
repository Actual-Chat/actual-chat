using ActualChat.Serialization.Internal;
using MemoryPack;
using MessagePack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackFormatter(typeof(ExpiringMessagePackFormatter<>))]
public sealed partial record Expiring<T>(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] T Value,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] Moment ExpiresAt = default);
