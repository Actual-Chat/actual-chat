namespace ActualChat.UI.Blazor.App.Services;

[MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial class FileMetadata
{
    [DataMember, MemoryPackOrder(0), Key(0)]
    public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(1), Key(1)]
    public string FileType { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)]
    public long Length { get; init; }
}
