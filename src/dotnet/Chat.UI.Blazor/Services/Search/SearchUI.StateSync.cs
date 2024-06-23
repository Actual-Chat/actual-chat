using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

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
            .Where(x => !x.HasError && !x.Value.Text.IsNullOrEmpty())
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
        var searchResultMap = await FindContacts(criteria, cancellationToken).ConfigureAwait(false);
        var foundContacts = new List<FoundContact>(searchResultMap.Sum(x => x.Value.Count));
        foreach (var scope in Scopes) {
            if (!searchResultMap.TryGetValue(scope, out var searchResults) || searchResults.Count == 0)
                continue;

            for (int i = 0; i < searchResults.Count; i++) {
                var searchResult = searchResults[i];
                foundContacts.Add(new FoundContact(searchResult, scope, i == 0, i == searchResults.Count - 1));
            }
        }
        _cached = new Cached(criteria, foundContacts);
        using (Invalidation.Begin()) {
            _ = GetContactSearchResults();
            _ = PseudoGetSearchMatch();
        }
    }

    private async Task<Dictionary<ContactSearchScope, IReadOnlyList<ContactSearchResult>>> FindContacts(
        Criteria criteria,
        CancellationToken cancellationToken)
    {
        if (criteria == Criteria.None)
            return [];

        var session = Session;
        var scopes = criteria.PlaceId.IsNone
            ? Scopes
            : Scopes.Where(x => x is not ContactSearchScope.Places);
        var subgroups = scopes.SelectMany(x => new SubgroupKey[] { new (x, true), new (x, false) }).ToArray();
        var allSearchResults = await subgroups.Select(Find)
            .CollectResults()
            .ConfigureAwait(false);
        var resultByScope = new Dictionary<ContactSearchScope, IReadOnlyList<ContactSearchResult>>();
        for (var i = 0; i < subgroups.Length; i++) {
            var (scope, _) = subgroups[i];
            var (searchResults, error) = allSearchResults[i];
            if (error is null)
                resultByScope[scope] = resultByScope.GetValueOrDefault(scope, []).Concat(searchResults.Hits).ToList();
        }
        return resultByScope;

        Task<ContactSearchResultPage> Find(SubgroupKey key)
            // TODO: reuse cached data for scope
            => Search.FindContacts(session, criteria.ToQuery(key), cancellationToken);
    }
}
