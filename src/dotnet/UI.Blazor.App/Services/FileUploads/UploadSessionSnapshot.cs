using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record UploadSessionSnapshot
{
    [DataMember, MemoryPackOrder(0), Key(0)] public string SessionId { get; set; } = "";
    [DataMember, MemoryPackOrder(1), Key(1)] public IFileProvider FileProvider { get; set; } = null!;
    //[DataMember, MemoryPackOrder(2)] public UploadStatus Status { get; set; } = UploadStatus.Pending; Obsolete
    [DataMember, MemoryPackOrder(3), Key(2)] public Moment CreatedAt { get; set; } = Moment.EpochStart;
    [DataMember, MemoryPackOrder(4), Key(3)] public Moment LastUpdatedAt { get; set; } = Moment.EpochStart;
    // [DataMember, MemoryPackOrder(5)] public ChatId ChatId { get; set; } = null!; Obsolete
    [DataMember, MemoryPackOrder(6), Key(4)] public UploadId? UploadId { get; set; }
    [DataMember, MemoryPackOrder(7), Key(5)] public MediaRef? MediaRef { get; set; }
    [DataMember, MemoryPackOrder(8), Key(6)] public int DataVersion { get; set; }
    [DataMember, MemoryPackOrder(9), Key(7)] public MetadataBag Metadata { get; set; }

    [DataMember, MemoryPackOrder(10), Key(8)] public UploadSessionState CurrentState { get; set; } = UploadSessionState.Created;
    [DataMember, MemoryPackOrder(11), Key(9)] public bool IsFailed { get; set; }
    [DataMember, MemoryPackOrder(12), Key(10)] public MediaId? ReservedMediaId { get; set; }
    [DataMember, MemoryPackOrder(13), Key(11)] public double StageProgress { get; set; }
    [DataMember, MemoryPackOrder(14), Key(12)] public string MediaScope { get; set; } = "";
    [DataMember, MemoryPackOrder(15), Key(13)] public FilePath? TranscodedFilePath { get; set; }
}
