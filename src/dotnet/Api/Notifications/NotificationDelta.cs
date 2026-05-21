namespace ActualChat.Notifications;

/// <summary>
/// Changes to a user's notification set that have not yet been pushed to the device.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationDelta(
    [property: DataMember(Order = 0), Key(0)] ApiArray<Notification> Upserts,
    [property: DataMember(Order = 1), Key(1)] ApiArray<NotificationId> DismissedIds
) {
    public static readonly NotificationDelta Empty = new(default, default);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => Upserts.Count == 0 && DismissedIds.Count == 0;
}
