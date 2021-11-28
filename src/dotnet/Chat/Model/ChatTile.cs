using System.Text.Json.Serialization;

namespace ActualChat.Chat;

public class ChatTile
{
    public Range<long> IdTileRange { get; init; }
    public Range<long> IdRange { get; init; }
    public Range<Moment> BeginsAtRange { get; init; }
    public ImmutableArray<ChatEntry> Entries { get; init; } = ImmutableArray<ChatEntry>.Empty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Entries.Length == 0;

    public ChatTile() { }

    public ChatTile(Range<long> idTileRange, ImmutableArray<ChatEntry> entries)
    {
        var idRange = new Range<long>(long.MaxValue, long.MinValue);
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var entry in entries) {
            idRange = idRange.MinMaxWith(entry.Id);
            beginsAtRange = beginsAtRange.MinMaxWith(entry.BeginsAt);
        }

        IdTileRange = idTileRange;
        Entries = entries;
        IdRange = idRange;
        BeginsAtRange = beginsAtRange;
    }

    public ChatTile(IEnumerable<ChatTile> tiles)
    {
        var entries = new List<ChatEntry>();
        var idTile = new Range<long>(long.MaxValue, long.MinValue);
        var idRange = new Range<long>(long.MaxValue, long.MinValue);
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var tile in tiles) {
            idTile = idTile.MinMaxWith(tile.IdTileRange);
            idRange = idRange.MinMaxWith(tile.IdRange);
            beginsAtRange = beginsAtRange.MinMaxWith(tile.BeginsAtRange);
            foreach (var entry in tile.Entries)
                entries.Add(entry);
        }

        IdTileRange = idTile;
        IdRange = idRange;
        BeginsAtRange = beginsAtRange;
        Entries = entries.ToImmutableArray();
    }
}
