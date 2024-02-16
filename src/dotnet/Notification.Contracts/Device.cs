using MemoryPack;

namespace ActualChat.Notification;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Device(
    [property: DataMember, MemoryPackOrder(0)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(1)] DeviceType DeviceType,
    [property: DataMember, MemoryPackOrder(2)] Moment CreatedAt,
    [property: DataMember, MemoryPackOrder(3)] Moment? AccessedAt);
