using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat;

/// <summary>
/// Application settings stored locally on the device.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record LocalAppSettings : IHasKvasKey<LocalAppSettings>
{
    [DataMember, MemoryPackOrder(0)] public bool? IsLogViewerEnabled { get; init; }
    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsLogViewerEnabledOrDefault => IsLogViewerEnabled ?? true;
}
