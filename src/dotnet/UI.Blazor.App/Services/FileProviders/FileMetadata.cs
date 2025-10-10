using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class FileMetadata
{
    [DataMember, MemoryPackOrder(0)]
    public string FileName { get; init; } = "";
    [DataMember, MemoryPackOrder(1)]
    public string FileType { get; init; } = "";
    [DataMember, MemoryPackOrder(2)]
    public long Length { get; init; }
}
