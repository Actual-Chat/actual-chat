using ActualLab.Versioning;

namespace ActualChat.Users;

/// <summary>
/// Extended session info with version tracking.
/// </summary>
[DataContract, MessagePackObject(AllowPrivate = true)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
public partial record SessionInfoFull(
    [property: DataMember(Order = 10), Key(10)] Session Session
    ) : SessionInfo(ReferenceEquals(Session, null) ? "" : Session.IdPrefix), IRequirementTarget, IHasVersion<long>
{
    // Order/Key 11 reserved (was Options, which carried GuestId) — do not reuse.
    [DataMember(Order = 13), Key(13)] public UserId? GuestId { get; init; }

    // MessagePack deserialization entry point: the int-keyed positional record ctor's first
    // parameter doesn't match Key(0)'s expected type, so MessagePack falls through to this
    // parameterless ctor and assigns each Key via the property initializers.
    [SerializationConstructor]
    internal SessionInfoFull() : this(default(Session)!) { }

    public SessionInfoFull(Session session, Moment createdAt = default)
        : this(session)
    {
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }
}
