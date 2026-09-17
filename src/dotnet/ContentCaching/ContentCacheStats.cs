namespace ActualChat.ContentCaching;

/// <summary>
/// What <see cref="FileSystemContentHandler"/> did with one request.
/// </summary>
public enum ContentCacheOutcome
{
    Hit = 0,
    JoinedFill = 1,
    StartedFill = 2,
    Bypass = 3,
    Error = 4,
}

/// <summary>
/// Thread-safe cumulative counters of a <see cref="FileSystemContentHandler"/>.
/// Served counts bytes handed to readers through the cache, fetched - bytes pulled downstream,
/// so a fill counts in both and a hit counts only as served.
/// </summary>
public sealed class ContentCacheStats
{
    private static readonly ContentCacheOutcome[] Outcomes = Enum.GetValues<ContentCacheOutcome>();

    private readonly long[] _outcomeCounts = new long[Outcomes.Length];
    private long _servedByteCount;
    private long _fetchedByteCount;

    public long ServedByteCount => Volatile.Read(ref _servedByteCount);
    public long FetchedByteCount => Volatile.Read(ref _fetchedByteCount);
    public long this[ContentCacheOutcome outcome] => Volatile.Read(ref _outcomeCounts[(int)outcome]);

    public void Report(ContentCacheOutcome outcome)
        => Interlocked.Increment(ref _outcomeCounts[(int)outcome]);

    public void ReportServed(long byteCount)
        => Interlocked.Add(ref _servedByteCount, byteCount);

    public void ReportFetched(long byteCount)
        => Interlocked.Add(ref _fetchedByteCount, byteCount);

    public override string ToString()
    {
        var outcomes = Outcomes.Select(x => $"{x}={this[x]}").ToDelimitedString(", ");
        return $"{outcomes}, Served={FileSizeFormatter.Format(ServedByteCount)}"
            + $", Fetched={FileSizeFormatter.Format(FetchedByteCount)}";
    }
}
