namespace ActualChat.Notifications;

/// <summary>
/// Represents a device registered for push notifications.
/// </summary>
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DataContract, MessagePackObject]
public sealed partial record Device(
    [property: DataMember(Order = 0), Key(0)] Symbol DeviceId,
    [property: DataMember(Order = 1), Key(1)] DeviceType DeviceType,
    [property: DataMember(Order = 2), Key(2)] Moment CreatedAt)
{
    [DataMember(Order = 3), Key(3)] public Moment? AccessedAt { get; init; }
}
