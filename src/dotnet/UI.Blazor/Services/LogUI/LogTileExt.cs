namespace ActualChat.UI.Blazor.Services;

public static class LogTileExt
{
    public static VirtualListTile<LogEntry> ToVirtualListTile(this LogTile logTile)
        => new (logTile.IdRange, logTile.Entries);

    public static IReadOnlyList<VirtualListTile<LogEntry>> ToVirtualListTiles(this IReadOnlyList<LogTile> logTiles)
        => [..logTiles.Select(x => x.ToVirtualListTile())];

    public static IReadOnlyList<LogEntry> ToLogEntries(this IReadOnlyList<LogTile> logTiles)
        => [..logTiles.SelectMany(x => x.Entries)];

    public static bool ContainStart(this IReadOnlyList<LogTile> logTiles, long startId)
        => logTiles.Count != 0 && logTiles[0].IdRange.Start <= startId;

    public static bool ContainEnd(this IReadOnlyList<LogTile> logTiles, long endIdExclusive)
        => logTiles.Count != 0 && logTiles[^1].IdRange.End >= endIdExclusive;
}
