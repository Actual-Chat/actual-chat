using ActualLab.Versioning;

namespace ActualChat.Users;

/// <summary>
/// Extended session info with version tracking.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public partial record SessionInfoFull(
    [property: DataMember(Order = 10), MemoryPackOrder(10)] Session Session
    ) : SessionInfo(Session.IdPrefix), IRequirementTarget, IHasVersion<long>
{
    [DataMember(Order = 11), MemoryPackOrder(11)] public ImmutableOptionSet Options { get; init; }

    public SessionInfoFull(Session session, Moment createdAt = default)
        : this(session)
    {
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }
}
