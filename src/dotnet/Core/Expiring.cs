using ActualChat.Serialization.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Expiring<T>(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] T Value,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] Moment ExpiresAt = default);
