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

    public static IQueryable<TSource> WhereIf<TSource>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, bool>> filter,
        bool condition)
        => condition ? source : source.Where(filter);
}
