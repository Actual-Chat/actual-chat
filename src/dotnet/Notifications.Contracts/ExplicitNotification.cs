using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// Represents a user-created notification (e.g., reminder, scheduled message).
/// </summary>
[DataContract, MessagePackObject]
public partial record ExplicitNotification(
    [property: DataMember(Order = 0), Key(0)] ExplicitNotificationId Id,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ) : IHasId<ExplicitNotificationId>, IHasVersion<long>
{
    [DataMember(Order = 2), Key(2)] public Moment CreatedAt { get; init; }
    [DataMember(Order = 3), Key(3)] public Moment UpdatedAt { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ExplicitNotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string SimilarityKey => Id.SimilarityKey;
}
