using MemoryPack;

namespace ActualChat.Notification;

[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Device(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol DeviceId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] DeviceType DeviceType,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] Moment CreatedAt)
{
    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(3)]
    private ApiNullable8<Moment> MemoryPackAccessedAt {
        get => AccessedAt;
        init => AccessedAt = value;
    }

    #endregion

    [DataMember(Order = 3), MemoryPackIgnore] public Moment? AccessedAt { get; init; }
}
