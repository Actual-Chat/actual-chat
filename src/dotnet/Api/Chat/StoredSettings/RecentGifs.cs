using ActualChat.Kvas;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record RecentGifs : StoredSettings, IHasOrigin, IHasKvasKey<RecentGifs>
{
    public const int MaxCount = 20;

    public static string KvasKey => nameof(RecentGifs);

    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public ApiArray<RecentGif> Items { get; init; } = []; // Most recent first

    // Most-recently-used list: the used GIF jumps to the front, pushing the rest down.
    public RecentGifs Use(RecentGif gif)
    {
        var items = new List<RecentGif>(Math.Min(Items.Count + 1, MaxCount)) { gif };
        foreach (var item in Items) {
            if (items.Count >= MaxCount)
                break;

            if (item.Slug != gif.Slug)
                items.Add(item);
        }
        return this with { Items = new ApiArray<RecentGif>(items) };
    }
}

[DataContract, MessagePackObject]
public sealed partial record RecentGif
{
    [DataMember, Key(0)] public string Slug { get; init; } = "";
    [DataMember, Key(1)] public string PreviewUrl { get; init; } = "";
    [DataMember, Key(2)] public int PreviewWidth { get; init; }
    [DataMember, Key(3)] public int PreviewHeight { get; init; }
    [DataMember, Key(4)] public string Url { get; init; } = "";
}
