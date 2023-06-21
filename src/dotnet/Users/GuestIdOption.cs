using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record GuestIdOption(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] UserId GuestId
    );
