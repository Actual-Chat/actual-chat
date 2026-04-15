using ActualChat.Kvas;

namespace ActualChat;

/// <summary>
/// Application settings stored locally on the device.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record LocalAppSettings : StoredSettings, IHasKvasKey<LocalAppSettings>
{
    [DataMember, MemoryPackOrder(0)] public bool? IsLogViewerEnabled { get; init; }
    [DataMember, MemoryPackOrder(1)] public string? SelectedCameraDeviceId { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? IsBackgroundBlurEnabled { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLogViewerEnabledOrDefault => IsLogViewerEnabled ?? true;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsBackgroundBlurEnabledOrDefault => IsBackgroundBlurEnabled ?? false;
}
