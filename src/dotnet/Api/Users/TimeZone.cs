using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record TimeZone(
    [property: DataMember, MemoryPackOrder(0)] string Id) : IHasId<string>
{
    [DataMember, MemoryPackOrder(1)] public string IanaName { get; set; } = "";
}
