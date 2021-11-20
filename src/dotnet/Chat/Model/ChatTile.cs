using ActualChat.Mathematics;

namespace ActualChat.Chat;

public record ChatTile
{
    public Range<long> IdTile { get; init; }
    public Range<long> IdRange { get; init; }
    public Range<Moment> BeginsAtRange { get; init; }
    public ImmutableArray<ChatEntry> Entries { get; init; } = ImmutableArray<ChatEntry>.Empty;
    public bool IsEmpty => Entries.Length == 0;

    public ChatTile(Range<long> idTile, ImmutableArray<ChatEntry> entries)
    {
        var idRange = new Range<long>(long.MaxValue, long.MinValue);
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var entry in entries) {
            idRange = idRange.MinMaxWith(entry.Id);
            beginsAtRange = beginsAtRange.MinMaxWith(entry.BeginsAt);
        }

        IdTile = idTile;
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
            idTile = idTile.MinMaxWith(tile.IdTile);
            idRange = idRange.MinMaxWith(tile.IdRange);
            beginsAtRange = beginsAtRange.MinMaxWith(tile.BeginsAtRange);
            foreach (var entry in tile.Entries)
                entries.Add(entry);
        }

        IdTile = idTile;
        IdRange = idRange;
        BeginsAtRange = beginsAtRange;
        Entries = entries.ToImmutableArray();
    }
}
