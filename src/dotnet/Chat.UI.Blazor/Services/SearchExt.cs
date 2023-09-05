using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class SearchExt
{
    public static IEnumerable<(TSource Source, double Rank)> WithSearchMatchRank<TSource>(
        this IEnumerable<TSource> source,
        SearchPhrase searchPhrase,
        Func<TSource, string> textSelector)
        => source.Select(c => (Source: c, Rank: searchPhrase.GetMatchRank(textSelector(c))));

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
