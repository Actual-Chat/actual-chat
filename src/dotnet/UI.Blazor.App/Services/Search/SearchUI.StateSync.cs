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
        List<FoundContact> foundContacts;
        var isSearchModeOn = !criteria.Text.IsNullOrEmpty();
        _isSearchModeOn.Value = isSearchModeOn;
        if (isSearchModeOn) {
            var searchResultMap = await FindContacts(criteria, cancellationToken).ConfigureAwait(false);
            foundContacts = new List<FoundContact>(searchResultMap.Sum(x => x.Value.Count));
            var expandableScopes =
                searchResultMap.Where(x => x.Value.Count >= Constants.Search.ContactSearchDefaultPageSize)
                    .Select(x => x.Key.Scope)
                    .Distinct()
                    .ToHashSet();

            foreach (var scope in Scopes) {
                var own = searchResultMap.GetValueOrDefault(new (scope, true));
                var other = searchResultMap.GetValueOrDefault(new (scope, false));
                var scopeResults = own.Concat(other).ToList();
                for (int i = 0; i < scopeResults.Count; i++) {
                    var searchResult = scopeResults[i];
                    foundContacts.Add(new (searchResult,
                        scope,
                        i == 0,
                        i == scopeResults.Count - 1,
                        own.Count >= Constants.Search.ContactSearchDefaultPageSize
                        || other.Count >= Constants.Search.ContactSearchDefaultPageSize));
                }
            }
        }
        else
            foundContacts = [];
        _cached = new Cached(criteria, foundContacts);
        using (Invalidation.Begin()) {
            _ = GetContactSearchResults();
            _ = PseudoGetSearchMatch();
        }
    }

    private async Task<Dictionary<SubgroupKey, ApiArray<ContactSearchResult>>> FindContacts(
        Criteria criteria,
        CancellationToken cancellationToken)
    {
        if (criteria == Criteria.None)
            return [];

        var session = Session;
        var scopes = criteria.PlaceId.IsNone
            ? Scopes
            : Scopes.Where(x => x is not ContactSearchScope.Places);
        var subgroups = ToSubgroups(scopes);
        var allSearchResults = await subgroups.Select(Find)
            .CollectResults()
            .ConfigureAwait(false);
        return subgroups.Zip(allSearchResults)
            .Where(x => x.Second.HasValue)
            .ToDictionary(x => x.First, x => x.Second.Value.Hits);

        Task<ContactSearchResultPage> Find(SubgroupKey key)
            // TODO: reuse cached data for scope
            => Search.FindContacts(session, criteria.ToQuery(key), cancellationToken);
    }

    private static SubgroupKey[] ToSubgroups(IEnumerable<ContactSearchScope> scopes)
        => scopes.SelectMany(x => new SubgroupKey[] { new (x, true), new (x, false) }).ToArray();
}
