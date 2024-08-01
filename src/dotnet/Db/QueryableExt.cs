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

    public static IQueryable<TSource> WhereIf<TSource>(this IQueryable<TSource> queryable, Expression<Func<TSource, bool>> filter, bool condition)
        => condition ? queryable.Where(filter) : queryable;

    public static async IAsyncEnumerable<TResult> ReadAsync<TSource, TResult>(
        this IOrderedQueryable<TSource> items,
        int pageSize,
        Expression<Func<TSource, TResult>> selector,
        [EnumeratorCancellation] CancellationToken token)
    {
        var skip = 0;
        while (true) {
            var count = 0;
            var page = items
                .Skip(skip)
                .Take(pageSize)
                .Select(selector)
                .ToAsyncEnumerable();

            await foreach (var item in page.WithCancellation(token).ConfigureAwait(false)) {
                count++;
                yield return item;
            }

            if (count < pageSize)
                break;

            skip += count;
        }
    }
}
