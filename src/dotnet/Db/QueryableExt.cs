using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db;

public static class QueryableExt
{
    // ToApiXxx

    public static Task<ApiArray<TSource>> ToApiArrayAsync<TSource>(
        this IQueryable<TSource> source,
        CancellationToken cancellationToken = default)
        => source.AsAsyncEnumerable().ToApiArrayAsync(cancellationToken);

    public static Task<ApiList<TSource>> ToApiListAsync<TSource>(
        this IQueryable<TSource> source,
        CancellationToken cancellationToken = default)
        => source.AsAsyncEnumerable().ToApiListAsync(cancellationToken);

    public static IQueryable<TSource> Log<TSource>(
        this IQueryable<TSource> source,
        ILogger log,
        [CallerMemberName] string context = "",
        LogLevel logLevel = LogLevel.Debug)
    {
        log.Log(logLevel, "{Context}: {Query}", context, source.ToQueryString());
        return source;
    }
}
