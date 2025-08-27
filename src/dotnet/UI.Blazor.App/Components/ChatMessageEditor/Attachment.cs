using ActualChat.Media;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Components;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Attachment(
    [property: DataMember, MemoryPackOrder(1)] int Id,
    [property: DataMember, MemoryPackOrder(2)] string Url,
    [property: DataMember, MemoryPackOrder(3)] string FileName,
    [property: DataMember, MemoryPackOrder(4)] string FileType,
    [property: DataMember, MemoryPackOrder(5)] int Length,
    [property: DataMember, MemoryPackOrder(6)] int Progress = 0,
    [property: DataMember, MemoryPackOrder(7)] MediaId? MediaId = null,
    [property: DataMember, MemoryPackOrder(8)] MediaId? ThumbnailMediaId = null)
{
    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsImage => MediaTypeExt.IsSupportedImage(FileType);
    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsVideo => MediaTypeExt.IsSupportedVideo(FileType);
    [IgnoreDataMember, MemoryPackIgnore]
    public bool Failed { get; init; }
    [IgnoreDataMember, MemoryPackIgnore]
    [MemberNotNullWhen(true, nameof(MediaId))]
    public bool Uploaded => Progress == 100 && MediaId != null;
}
