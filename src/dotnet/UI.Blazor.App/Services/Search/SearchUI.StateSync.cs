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
            .AdjacentDistinct();
        var lastText = "";
        var debouncer = Debouncer.New<Criteria>(DebounceInterval, UpdateSearchResults);
        await using var _ = debouncer.ConfigureAwait(false);
        await foreach (var criteria in criteriaChanges.ConfigureAwait(false)) {
            if (criteria.Text != lastText) {
                lastText = criteria.Text;
                _isGlobalSearchOn.Value = false;
            }
            debouncer.Enqueue(criteria);
        }
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
            if (criteria.LocationFilter == SearchLocationFilter.Anywhere) {
                if (criteria.IsGlobalSearchOn)
                    AddGlobalSearchResults(searchResultMap);
                else
                    AddGlobalSearchPlaceholder();
            }
        }
        _isSearchModeOn.Value = !criteria.Text.IsNullOrEmpty();
        _isResultsNavigationOn.Value = false;
        _cached = new Cached(foundItems);
        var messageSearchMatches = _cached.FoundItems
            .Where(x => x.Scope is SearchScope.Messages && !x.IsGlobalSearchPlaceholder)
            .ToDictionary(x => x.EntryId!, IReadOnlySet<string> (x) => x.HighlightedWords);
        HighlightUI.Set(messageSearchMatches);
        _selectedItem.Invalidate();
        using (Invalidation.Begin())
            _ = GetSearchResults();
        return;

        void AddUserRelatedSearchResults(Dictionary<SubgroupKey, IReadOnlyList<IHasSearchMatch>> searchResultMap) {
            foreach (var scope in Scopes) {
                var scopeResults = searchResultMap.GetValueOrDefault(new (scope, true)) ?? [];
                var displayLimit = criteria.DisplayLimit(scope);
                var hasMore = scopeResults.Count > displayLimit;
                var isExpanded = criteria.ExtendedLimits.Contains(scope);
                var canExpandOrCollapse = isExpanded || hasMore;
                var displayCount = Math.Min(scopeResults.Count, displayLimit);
                for (int i = 0; i < displayCount; i++) {
                    foundItems.Add(new (scopeResults[i],
                        scope,
                        false,
                        i == 0,
                        i == displayCount - 1,
                        canExpandOrCollapse));
                }
            }
        }

        void AddGlobalSearchResults(Dictionary<SubgroupKey, IReadOnlyList<IHasSearchMatch>> searchResultMap) {
            var perScope = Scopes.Select(scope => {
                var rs = searchResultMap.GetValueOrDefault(new (scope, false)) ?? [];
                var dl = criteria.DisplayLimit(scope);
                return (Scope: scope, Results: rs, DisplayCount: Math.Min(rs.Count, dl), HasMore: rs.Count > dl);
            }).ToArray();
            var totalDisplayCount = perScope.Sum(p => p.DisplayCount);
            var anyHasMore = perScope.Any(p => p.HasMore);
            var anyExpanded = Scopes.Any(s => criteria.ExtendedLimits.Contains(s));
            var canExpandOrCollapse = anyHasMore || anyExpanded;
            var i = 0;
            foreach (var p in perScope) {
                for (var j = 0; j < p.DisplayCount; j++) {
                    foundItems.Add(new (p.Results[j],
                        p.Scope,
                        true,
                        i == 0,
                        i == totalDisplayCount - 1,
                        canExpandOrCollapse));
                    i++;
                }
            }
        }

        void AddGlobalSearchPlaceholder() {
            foundItems.Add(new (
                null!,
                SearchScope.People,
                true,
                IsFirstInGroup: true,
                IsLastInGroup: true,
                IsGlobalSearchPlaceholder: true));
        }
    }

    private async Task<Dictionary<SubgroupKey, IReadOnlyList<IHasSearchMatch>>> Find(
        Criteria criteria,
        CancellationToken cancellationToken)
    {
        if (criteria == Criteria.None || criteria is { LocationFilter: SearchLocationFilter.Chat, ChatId: null })
            return [];

        var session = Session;
        IEnumerable<SearchScope> scopes;
        if (criteria.LocationFilter == SearchLocationFilter.Chat)
            scopes = [SearchScope.Messages];
        else if (criteria.PlaceId is null)
            scopes = Scopes;
        else
            scopes = Scopes.Where(x => x is not SearchScope.Places);
        var subgroups = ToSubgroups(scopes, criteria.TypeFilter, criteria.IsGlobalSearchOn);
        var allSearchResults = await subgroups.Select(FindSubgroup)
            .CollectResults(ApiConstants.Concurrency.Low, cancellationToken)
            .ConfigureAwait(false);
        return subgroups.Zip(allSearchResults)
            .Where(x => x.Second.HasValue)
            .ToDictionary(x => x.First, x => x.Second.Value);

        async Task<IReadOnlyList<IHasSearchMatch>> FindSubgroup(SubgroupKey key) {
            // Own people & groups are served from fast in-memory local search; global results,
            // place-scoped results, places and messages stay on the server.
            if (key is { Own: true, Scope: SearchScope.People or SearchScope.Groups } && criteria.PlaceId is null)
                return await LocalSearch
                    .FindContacts(key.Scope, criteria.Text, criteria.DisplayLimit(key.Scope) + 1, cancellationToken)
                    .ConfigureAwait(false);

            // TODO: reuse cached data for scope
            switch (key.Scope) {
            case SearchScope.People:
            case SearchScope.Groups:
            case SearchScope.Places:
                var contactsPage = await Search
                    .FindContacts(session, criteria.ToContactQuery(key), cancellationToken)
                    .ConfigureAwait(false);
                return contactsPage.Items;
            case SearchScope.Messages:
                var entriesPage = await Search
                    .FindEntries(session, criteria.ToEntryQuery(key), cancellationToken)
                    .ConfigureAwait(false);
                return entriesPage.Items;
            default:
                throw new ArgumentOutOfRangeException($"{nameof(key)}.{nameof(key.Scope)}");
            }
        }
    }

    private static SubgroupKey[] ToSubgroups(
        IEnumerable<SearchScope> scopes,
        SearchTypeFilter typeFilter,
        bool includeGlobal)
    {
        var filteredScopes = typeFilter switch {
            SearchTypeFilter.People => scopes.Where(x => x == SearchScope.People),
            SearchTypeFilter.Groups => scopes.Where(x => x is SearchScope.Groups or SearchScope.Places),
            SearchTypeFilter.Messages => scopes.Where(x => x == SearchScope.Messages),
            _ => scopes,
        };
        var subgroups = includeGlobal
            ? filteredScopes.SelectMany(x => new SubgroupKey[] { new (x, true), new (x, false) })
            : filteredScopes.Select(x => new SubgroupKey(x, true));
        return subgroups
            .Except([new SubgroupKey(SearchScope.Messages, false)]) // searching for messages only in own chats
            .ToArray();
    }
}
