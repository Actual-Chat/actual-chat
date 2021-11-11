namespace ActualChat.Chat;

/* WIP(AY)

public record ChatPage
{
    public Range<long> IdTile { get; init; }
    public long MinId { get; init; }
    public long MaxId { get; init; }
    public Moment MinCreatedAt { get; init; }
    public Moment MaxCreatedAt { get; init; }
    ImmutableArray<ChatEntry> Entries { get; init; }

    public static ChatPage Combine(IEnumerable<ChatPage> pages)
    {
        var entries = new List<ChatEntry>();
        var minTileId = long.MaxValue;
        var maxTileId = long.MinValue;
        var minId = long.MaxValue;
        var maxId = long.MinValue;
        var minCreatedAt = Moment.MaxValue;
        var maxCreatedAt = Moment.MinValue;

        foreach (var page in pages) {
            minTileId = Math.Min(minTileId, page.IdTile.Start);
            maxTileId = Math.Max(maxTileId, page.IdTile.End);
            minId = Math.Min(minId, page.MinId);
            maxId = Math.Min(maxId, page.MaxId);
            minCreatedAt = Moment.Min(minCreatedAt, page.MinCreatedAt);
            maxCreatedAt = Moment.Min(maxCreatedAt, page.MaxCreatedAt);
        }
        return new ChatPage();
    }
}

*/
