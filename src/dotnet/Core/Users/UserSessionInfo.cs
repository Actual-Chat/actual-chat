namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public sealed partial record UserSessionInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string IdPrefix)
{
    [DataMember(Order = 1), MemoryPackOrder(1)] public bool IsApiKey { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2)] public bool IsActive { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3)] public string Name { get; init; } = "";
    [DataMember(Order = 4), MemoryPackOrder(4)] public string UserAgent { get; init; } = "";
    [DataMember(Order = 5), MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6)] public Moment LastSeenAt { get; init; }
    [DataMember(Order = 7), MemoryPackOrder(7)] public Moment? ExpiresAt { get; init; }
}
