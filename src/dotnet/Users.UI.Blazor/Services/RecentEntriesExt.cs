namespace ActualChat.Users.UI.Blazor.Services;

public static class RecentEntriesExt
{
    public static async Task<ImmutableArray<T>> OrderByRecency<T>(
        this IRecentEntries recentEntries,
        Session session,
        IReadOnlyCollection<T> items,
        RecentScope scope,
        CancellationToken cancellationToken)
        where T : IHasId<Symbol>
    {
        var recent = await recentEntries.List(session, scope, items.Count, cancellationToken).ConfigureAwait(false);
        var recentMap = recent.ToImmutableDictionary(x => (Symbol)x.Key, x => x.UpdatedAt);

        return items.OrderByDescending(x => recentMap.GetValueOrDefault(x.Id, Moment.MinValue)).ToImmutableArray();
    }
}
