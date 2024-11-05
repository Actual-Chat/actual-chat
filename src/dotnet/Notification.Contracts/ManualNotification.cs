using MemoryPack;
using ActualLab.Versioning;

namespace ActualChat.Notification;

#pragma warning disable MA0049 // Allows ActualChat.Notification.Notification

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ManualNotification(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ManualNotificationId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ManualNotificationId>, IHasVersion<long>
{
    [DataMember(Order = 2), MemoryPackOrder(2)] public Moment CreatedAt { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3)] public Moment UpdatedAt { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId UserId => Id.UserId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ManualNotificationKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol SimilarityKey => Id.SimilarityKey;
}
