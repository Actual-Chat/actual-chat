namespace ActualChat.Users;

/// <summary>
/// Represents a time zone with Windows and IANA identifiers.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record TimeZone(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Id) : IHasId<string>
{
    [DataMember, MemoryPackOrder(1), Key(1)] public string IanaName { get; set; } = "";
}
