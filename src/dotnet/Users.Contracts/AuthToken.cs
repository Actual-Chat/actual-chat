using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AuthToken(
    [property: DataMember, MemoryPackOrder(0)]
    string Value,
    [property: DataMember, MemoryPackOrder(1)]
    Moment ExpiresAt)
{
    public static AuthToken None { get; } = new ("~", Moment.MaxValue);
}
