namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: ConstructorShape, JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record SessionInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] string IdPrefix)
{
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)] public long Version { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)] public bool IsActive { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)] public Moment CreatedAt { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)] public Moment LastSeenAt { get; init; }
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)] public Moment ExpiresAt { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)] public UserIdentity AuthenticatedIdentity { get; init; }
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)] public UserId? UserId { get; init; }
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)] public string Description { get; init; } = "";
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)] public string IPAddress { get => field ?? ""; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public SessionKind Kind => IdPrefix.StartsWith(CoreConstants.Session.ApiKeyPrefix)
        ? SessionKind.ApiKey
        : SessionKind.Session;
}
