using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserPicture(
    [property: DataMember, MemoryPackOrder(0)] string? ContentId,
    [property: DataMember, MemoryPackOrder(1)] string? Picture);
