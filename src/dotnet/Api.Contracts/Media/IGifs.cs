using ActualLab.Rpc;

namespace ActualChat.Media;

public interface IGifs : IRpcService
{
    Task<GifSearchResult> Search(string query, int page, CancellationToken cancellationToken);
    Task<GifSearchResult> GetTrending(int page, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record GifSearchResult(
    [property: DataMember, MemoryPackOrder(0), Key(0)] GifItem[] Items,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool HasNext);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record GifItem(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Slug,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Title,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string PreviewUrl,
    [property: DataMember, MemoryPackOrder(3), Key(3)] int PreviewWidth,
    [property: DataMember, MemoryPackOrder(4), Key(4)] int PreviewHeight,
    [property: DataMember, MemoryPackOrder(5), Key(5)] string Url,
    [property: DataMember, MemoryPackOrder(6), Key(6)] int Width,
    [property: DataMember, MemoryPackOrder(7), Key(7)] int Height,
    [property: DataMember, MemoryPackOrder(8), Key(8)] string BlurPreview);
