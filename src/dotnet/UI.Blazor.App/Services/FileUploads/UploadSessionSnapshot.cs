using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MessagePackObject]
public partial record UploadSessionSnapshot
{
    [DataMember, Key(0)] public string SessionId { get; set; } = "";
    [DataMember, Key(1)] public IFileProvider FileProvider { get; set; } = null!;
    //[DataMember, MemoryPackOrder(2)] public UploadStatus Status { get; set; } = UploadStatus.Pending; Obsolete
    [DataMember, Key(2)] public Moment CreatedAt { get; set; } = Moment.EpochStart;
    [DataMember, Key(3)] public Moment LastUpdatedAt { get; set; } = Moment.EpochStart;
    // [DataMember, MemoryPackOrder(5)] public ChatId ChatId { get; set; } = null!; Obsolete
    [DataMember, Key(4)] public UploadId? UploadId { get; set; }
    [DataMember, Key(5)] public MediaRef? MediaRef { get; set; }
    [DataMember, Key(6)] public int DataVersion { get; set; }
    [DataMember, Key(7)] public MetadataBag Metadata { get; set; }

    [DataMember, Key(8)] public UploadSessionState CurrentState { get; set; } = UploadSessionState.Created;
    [DataMember, Key(9)] public bool IsFailed { get; set; }
    [DataMember, Key(10)] public MediaId? ReservedMediaId { get; set; }
    [DataMember, Key(11)] public double StageProgress { get; set; }
    [DataMember, Key(12)] public string MediaScope { get; set; } = "";
    [DataMember, Key(13)] public FilePath? TranscodedFilePath { get; set; }
}
