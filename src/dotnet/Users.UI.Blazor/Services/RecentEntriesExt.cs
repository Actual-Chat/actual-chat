namespace ActualChat.Users.UI.Blazor.Services;

public static class RecentEntriesExt
{
    public static Task<ImmutableArray<T>> OrderByRecency<T>(
        this IRecentEntries recentEntries,
        Session session,
        IReadOnlyCollection<T> items,
        RecencyScope scope,
        CancellationToken cancellationToken)
        where T : IHasId<Symbol>
        => OrderByRecency(recentEntries,
            session,
            items,
            scope,
            items.Count,
            cancellationToken);

    public static async Task<ImmutableArray<T>> OrderByRecency<T>(
        this IRecentEntries recentEntries,
        Session session,
        IReadOnlyCollection<T> items,
        RecencyScope scope,
        int limit,
        CancellationToken cancellationToken)
        where T : IHasId<Symbol>
    {
        var recent = await recentEntries.List(session, scope, limit, cancellationToken).ConfigureAwait(false);
        var recentMap = recent.ToDictionary(x => (Symbol)x.Key, x => x.UpdatedAt);
        return items.OrderByDescending(x => recentMap.GetValueOrDefault(x.Id, Moment.MinValue)).ToImmutableArray();
    }
}
