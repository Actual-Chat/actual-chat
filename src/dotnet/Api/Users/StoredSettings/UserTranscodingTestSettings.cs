using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserTranscodingTestSettings : StoredSettings, IHasKvasKey<UserTranscodingTestSettings>
{
    [DataMember, MemoryPackOrder(0), Key(0)] public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(1), Key(1)] public UploadId? UploadId { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public MediaId? MediaId { get; init; }
}
