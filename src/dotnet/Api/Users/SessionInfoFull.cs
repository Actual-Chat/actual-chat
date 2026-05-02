using ActualLab.Versioning;

namespace ActualChat.Users;

/// <summary>
/// Extended session info with version tracking.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record SessionInfoFull(
    [property: DataMember(Order = 10), MemoryPackOrder(10), Key(10)] Session Session
    ) : SessionInfo(ReferenceEquals(Session, null) ? "" : Session.IdPrefix), IRequirementTarget, IHasVersion<long>
{
    [DataMember(Order = 11), MemoryPackOrder(11), Key(11)] public ImmutableOptionSet Options { get; init; }

    internal SessionInfoFull() : this(default(Session)!) { }

    public SessionInfoFull(Session session, Moment createdAt = default)
        : this(session)
    {
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }
}
