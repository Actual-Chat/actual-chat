namespace ActualChat.Search;

public static class SearchExt
{
    public static IEnumerable<(TSource Source, double Rank)> WithSearchMatchRank<TSource>(
        this IEnumerable<TSource> source,
        MemSearchQuery query,
        Func<TSource, MemSearchDocument> documentSelector)
        => source.Select(c => (Source: c, Rank: documentSelector(c).GetCoverageScore(query)));

    public static IEnumerable<TSource> WithoutSearchMatchRank<TSource>(
        this IEnumerable<(TSource Source, double Rank)> rankedSource)
        => rankedSource.Select(c => c.Source);

    public static IEnumerable<(TSource Source, double Rank)> FilterBySearchMatchRank<TSource>(
        this IEnumerable<(TSource Source, double Rank)> rankedSource)
        => rankedSource.Where(x => x.Rank > 0);

    public static IEnumerable<(TSource Source, double Rank)> OrderBySearchMatchRank<TSource>(
        this IEnumerable<(TSource Source, double Rank)> rankedChats)
        => rankedChats.OrderByDescending(x => x.Rank);
}
