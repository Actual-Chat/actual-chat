namespace ActualChat.UI.Blazor.Services;

public sealed record LogTile(Range<long> IdRange, IReadOnlyList<LogEntry> Entries)
{
    public static LogTile Empty => new (new (0, 0), []);

    public bool IsEmpty => Entries.Count == 0;

    public static LogTile From(LogTile[] smallerTiles)
    {
        if (smallerTiles.Length == 0)
            return Empty;

        var idTile = smallerTiles.Aggregate(new Range<long>(long.MaxValue, long.MinValue),
            (t1, t2) => t1.MinMaxWith(t2.IdRange));
        var entries = smallerTiles.SelectMany(x => x.Entries).ToList();
        return new LogTile(idTile, entries);
    }
}
