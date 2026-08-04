namespace ActualChat.UI.Blazor.App.Services;

[MessagePackObject]
public partial class FileMetadata
{
    [DataMember, Key(0)]
    public string FileName { get; init; } = "";
    [DataMember, Key(1)]
    public string FileType { get; init; } = "";
    [DataMember, Key(2)]
    public long Length { get; init; }
}
