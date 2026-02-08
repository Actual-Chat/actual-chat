using ActualChat.Kvas;

namespace ActualChat;

/// <summary>
/// Application settings stored locally on the device.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record LocalAppSettings : IHasKvasKey<LocalAppSettings>
{
    [DataMember, MemoryPackOrder(0)] public bool? IsLogViewerEnabled { get; init; }
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLogViewerEnabledOrDefault => IsLogViewerEnabled ?? true;
}
