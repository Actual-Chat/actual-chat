namespace ActualChat.Notification.Backend;

[DataContract]
public sealed record Device(
    [property: DataMember] Symbol DeviceId,
    [property: DataMember] DeviceType DeviceType,
    [property: DataMember] Moment CreatedAt,
    [property: DataMember] Moment? AccessedAt);
