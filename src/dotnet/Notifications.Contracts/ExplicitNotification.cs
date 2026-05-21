using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// Represents a user-created notification (e.g., reminder, scheduled message).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ExplicitNotification(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] ExplicitNotificationId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<ExplicitNotificationId>, IHasVersion<long>
{
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)] public Moment CreatedAt { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)] public Moment UpdatedAt { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ExplicitNotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;
}
