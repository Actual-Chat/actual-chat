namespace ActualChat.Notification;

/// <summary>
/// Represents a device registered for push notifications.
/// </summary>
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record Device(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] Symbol DeviceId,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] DeviceType DeviceType,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] Moment CreatedAt)
{
    #region MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(3)]
    private ApiNullable8<Moment> MemoryPackAccessedAt {
        get => AccessedAt;
        init => AccessedAt = value;
    }

    #endregion

    [DataMember(Order = 3), MemoryPackIgnore, Key(3)] public Moment? AccessedAt { get; init; }
    [DataMember(Order = 4), MemoryPackIgnore, Key(4)] public NotificationChannel NotificationChannel { get; init; } = NotificationChannel.Text;
}
