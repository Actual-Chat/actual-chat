using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record UploadSessionSnapshot
{
    [DataMember, MemoryPackOrder(0)] public string SessionId { get; set; } = "";
    [DataMember, MemoryPackOrder(1)] public IFileProvider FileProvider { get; set; } = null!;
    //[DataMember, MemoryPackOrder(2)] public UploadStatus Status { get; set; } = UploadStatus.Pending; Obsolete
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; set; } = Moment.EpochStart;
    [DataMember, MemoryPackOrder(4)] public Moment LastUpdatedAt { get; set; } = Moment.EpochStart;
    // [DataMember, MemoryPackOrder(5)] public ChatId ChatId { get; set; } = null!; Obsolete
    [DataMember, MemoryPackOrder(6)] public UploadId? UploadId { get; set; }
    [DataMember, MemoryPackOrder(7)] public MediaRef? MediaRef { get; set; }
    [DataMember, MemoryPackOrder(8)] public int DataVersion { get; set; }
    [DataMember, MemoryPackOrder(9)] public PropertyBag Metadata { get; set; }

    [DataMember, MemoryPackOrder(10)] public UploadSessionState CurrentState { get; set; } = UploadSessionState.Created;
    [DataMember, MemoryPackOrder(11)] public bool IsFailed { get; set; }
    [DataMember, MemoryPackOrder(12)] public MediaId? ReservedMediaId { get; set; }
    [DataMember, MemoryPackOrder(13)] public double StageProgress { get; set; }
    [DataMember, MemoryPackOrder(14)] public string MediaScope { get; set; } = "";
    [DataMember, MemoryPackOrder(15)] public FilePath? TranscodedFilePath { get; set; }
}
