using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public partial class SearchUI
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(0.5);

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
            .Select(x => x.Value)
            .DeduplicateNeighbors();
        var debouncer = Debouncer.New<Criteria>(DebounceInterval, UpdateSearchResults);
        await using var _ = debouncer.ConfigureAwait(false);
        await foreach (var criteria in criteriaChanges.ConfigureAwait(false))
            debouncer.Enqueue(criteria);
    }

    private async Task UpdateSearchResults(
        Criteria criteria,
        CancellationToken cancellationToken)
    {
        List<FoundItem> foundItems = [];
        if (!criteria.Text.IsNullOrEmpty()) {
            using var searchCts = cancellationToken.CreateLinkedTokenSource(SearchTimeout);
            var searchResultMap = await Find(criteria, searchCts.Token).ConfigureAwait(false);
            foundItems = new List<FoundItem>(searchResultMap.Sum(x => x.Value.Count));

            AddUserRelatedSearchResults(searchResultMap);
            AddGlobalSearchResults(searchResultMap);
        }
        _isSearchModeOn.Value = !criteria.Text.IsNullOrEmpty();
        _isResultsNavigationOn.Value = false;
        _cached = new Cached(foundItems);
        using (Invalidation.Begin())
            _ = GetSearchResults();
        return;

        void AddUserRelatedSearchResults(Dictionary<SubgroupKey, IReadOnlyList<SearchResult>> searchResultMap)
        {
            foreach (var scope in Scopes) {
                var scopeResults = searchResultMap.GetValueOrDefault(new (scope, true)) ?? [];
                for (int i = 0; i < scopeResults.Count; i++) {
                    var searchResult = scopeResults[i];
                    foundItems.Add(new (searchResult,
                        scope,
                        false,
                        i == 0,
                        i == scopeResults.Count - 1,
                        scopeResults.Count >= Constants.Search.DefaultPageSize));
                }
            }
        }

        void AddGlobalSearchResults(Dictionary<SubgroupKey, IReadOnlyList<SearchResult>> searchResultMap)
        {
            var globalSearchResultCount = Scopes.Sum(x => searchResultMap.GetValueOrDefault(new (x, false))?.Count ?? 0);
            var canGlobalSearchResultsBeExpanded = Scopes.Any(x => searchResultMap.GetValueOrDefault(new (x, false))?.Count >= Constants.Search.DefaultPageSize);
            var i = 0;
            foreach (var scope in Scopes) {
                var scopeResults = searchResultMap.GetValueOrDefault(new (scope, false)) ?? [];
                foreach (var searchResult in scopeResults) {
                    foundItems.Add(new (searchResult,
                        scope,
                        true,
                        i == 0,
                        i == globalSearchResultCount - 1,
                        canGlobalSearchResultsBeExpanded));
                    i++;
                }
            }
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
            .CollectResults(ApiConstants.Concurrency.Low, cancellationToken)
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
                throw new ArgumentOutOfRangeException($"{nameof(key)}.{nameof(key.Scope)}");
            }
        }
    }

    private static SubgroupKey[] ToSubgroups(IEnumerable<SearchScope> scopes)
        => scopes.SelectMany(x => new SubgroupKey[] { new (x, true), new (x, false) })
            .Except([new SubgroupKey(SearchScope.Messages, false)]) // searching for messages only in own chats
            .ToArray();
}
