using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public partial class SearchUI
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(30);

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncSearch),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, (ILogger?)Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task SyncSearch(CancellationToken cancellationToken)
    {
        var cCriteria = await Computed.Capture(() => GetCriteria(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var criteriaChanges = cCriteria.Changes(cancellationToken)
            .Where(x => !x.HasError)
            .Select(x => x.Value);
        DelegatingWorker? activeSearchJob = null;
        try {
            await foreach (var criteria in criteriaChanges.ConfigureAwait(false)) {
                if (activeSearchJob != null)
                    await activeSearchJob.DisposeSilentlyAsync().ConfigureAwait(false);
                activeSearchJob = DelegatingWorker.New(ct => UpdateSearchResults(criteria, ct),
                    cancellationToken.CreateLinkedTokenSource(SearchTimeout));
            }
        }
        finally {
            await activeSearchJob.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    private async Task UpdateSearchResults(
        Criteria criteria,
        CancellationToken cancellationToken)
    {
        List<FoundItem> foundItems = [];
        var isSearchModeOn = !criteria.Text.IsNullOrEmpty();
        _isSearchModeOn.Value = isSearchModeOn;
        if (isSearchModeOn) {
            var searchResultMap = await Find(criteria, cancellationToken).ConfigureAwait(false);
            foundItems = new List<FoundItem>(searchResultMap.Sum(x => x.Value.Count));

            foreach (var scope in Scopes) {
                var own = searchResultMap.GetValueOrDefault(new (scope, true)) ?? [];
                var other = searchResultMap.GetValueOrDefault(new (scope, false)) ?? [];
                var scopeResults = own.Concat(other).ToList();
                for (int i = 0; i < scopeResults.Count; i++) {
                    var searchResult = scopeResults[i];
                    foundItems.Add(new (searchResult,
                        scope,
                        i == 0,
                        i == scopeResults.Count - 1,
                        own.Count >= Constants.Search.DefaultPageSize
                        || other.Count >= Constants.Search.DefaultPageSize));
                }
            }
        }
        _cached = new Cached(criteria, foundItems);
        using (Invalidation.Begin()) {
            _ = GetSearchResults();
            _ = GetContactSearchResults();
        }
    }

    private async Task<Dictionary<SubgroupKey, IReadOnlyList<SearchResult>>> Find(
        Criteria criteria,
        CancellationToken cancellationToken)
    {
        if (criteria == Criteria.None)
            return [];

        var session = Session;
        var scopes = criteria.PlaceId.IsNone
            ? Scopes
            : Scopes.Where(x => x is not SearchScope.Places);
        var subgroups = ToSubgroups(scopes);
        var allSearchResults = await subgroups.Select(FindSubgroup)
            .CollectResults()
            .ConfigureAwait(false);
        return subgroups.Zip(allSearchResults)
            .Where(x => x.Second.HasValue)
            .ToDictionary(x => x.First, x => x.Second.Value);

        async Task<IReadOnlyList<SearchResult>> FindSubgroup(SubgroupKey key) // TODO: reuse cached data for scope
        {
            switch (key.Scope) {
            case SearchScope.People:
            case SearchScope.Groups:
            case SearchScope.Places:
                var contactResponse = await Search.FindContacts(session, criteria.ToContactQuery(key), cancellationToken)
                    .ConfigureAwait(false);
                return contactResponse.Hits;
            case SearchScope.Messages:
                var entryResponse = await Search.FindEntries(session, criteria.ToEntryQuery(key), cancellationToken).ConfigureAwait(false);
                return entryResponse.Hits;
            default:
                throw new ArgumentOutOfRangeException(nameof(key.Scope), key.Scope, null);
            }
        }
    }

    private static SubgroupKey[] ToSubgroups(IEnumerable<SearchScope> scopes)
        => scopes.SelectMany(x => new SubgroupKey[] { new (x, true), new (x, false) })
            .Except([new SubgroupKey(SearchScope.Messages, false)]) // searching for messages only in own chats
            .ToArray();
}
