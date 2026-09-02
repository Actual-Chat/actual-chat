namespace ActualChat.UI.Blazor.Services;

public sealed record LogTile(Range<long> IdRange, IReadOnlyList<LogEntry> Entries)
{
    public static LogTile Empty => new (new (0, 0), []);

    public bool IsEmpty => Entries.Count == 0;
}
