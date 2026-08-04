using ActualLab.Rpc;

namespace ActualChat.Media;

public interface IGifs : IRpcService
{
    Task<GifSearchResult> Search(string query, int page, CancellationToken cancellationToken);
    Task<GifSearchResult> GetTrending(int page, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
public sealed partial record GifSearchResult(
    [property: DataMember, Key(0)] GifItem[] Items,
    [property: DataMember, Key(1)] bool HasNext);

[DataContract, MessagePackObject]
public sealed partial record GifItem(
    [property: DataMember, Key(0)] string Slug,
    [property: DataMember, Key(1)] string Title,
    [property: DataMember, Key(2)] string PreviewUrl,
    [property: DataMember, Key(3)] int PreviewWidth,
    [property: DataMember, Key(4)] int PreviewHeight,
    [property: DataMember, Key(5)] string Url,
    [property: DataMember, Key(6)] int Width,
    [property: DataMember, Key(7)] int Height,
    [property: DataMember, Key(8)] string BlurPreview);
