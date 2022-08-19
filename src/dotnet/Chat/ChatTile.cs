namespace ActualChat.Chat;

public class ChatTile
{
    public Range<long> IdTileRange { get; init; }
    public bool IncludesRemoved { get; init; }
    public Range<Moment> BeginsAtRange { get; init; }
    public ImmutableArray<ChatEntry> Entries { get; init; } = ImmutableArray<ChatEntry>.Empty; // Always sorted by Id!
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Entries.Length == 0;

    public ChatTile() { }

    public ChatTile(Range<long> idTileRange, bool includesRemoved, ImmutableArray<ChatEntry> entries)
    {
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var entry in entries)
            beginsAtRange = beginsAtRange.MinMaxWith(entry.BeginsAt);

        IdTileRange = idTileRange;
        IncludesRemoved = includesRemoved;
        BeginsAtRange = (beginsAtRange.Start, beginsAtRange.End + TimeSpan.FromTicks(1));
        Entries = entries;
    }

    public ChatTile(IEnumerable<ChatTile> tiles, bool includesRemoved)
    {
        var entries = new List<ChatEntry>();
        var idTile = new Range<long>(long.MaxValue, long.MinValue);
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var tile in tiles) {
            idTile = idTile.MinMaxWith(tile.IdTileRange);
            beginsAtRange = beginsAtRange.MinMaxWith(tile.BeginsAtRange);
            foreach (var entry in tile.Entries)
                entries.Add(entry);
        }

        IdTileRange = idTile;
        IncludesRemoved = includesRemoved;
        BeginsAtRange = (beginsAtRange.Start, beginsAtRange.End + TimeSpan.FromTicks(1));
        Entries = entries.ToImmutableArray();
    }
}
