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
            .Where(x => !x.HasError && !x.Value.Criteria.IsNullOrEmpty())
            .Select(x => x.Value);
        DelegatingWorker? activeSearchJob = null;
        try {
            await foreach (var (criteria, placeId) in criteriaChanges.ConfigureAwait(false)) {
                if (activeSearchJob != null)
                    await activeSearchJob.DisposeSilentlyAsync().ConfigureAwait(false);
                activeSearchJob = DelegatingWorker.New(ct => UpdateSearchResults(criteria, placeId, ct),
                    cancellationToken.CreateLinkedTokenSource(SearchTimeout));
            }
        }
        finally {
            await activeSearchJob.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    private async Task UpdateSearchResults(string criteria, PlaceId placeId, CancellationToken cancellationToken)
    {
        var results = await FindContacts(criteria, placeId, cancellationToken).ConfigureAwait(false);
        _contactSearchResults = new Cached(results);
        using (Invalidation.Begin()) {
            _ = GetContactSearchResults();
            _ = PseudoGetSearchMatch();
        }
    }

    private async Task<IReadOnlyList<ContactSearchResult>> FindContacts(
        string criteria,
        PlaceId? placeId,
        CancellationToken cancellationToken)
    {
        if (criteria.IsNullOrEmpty())
            return [];

        var session = Hub.Session();
        if (placeId == PlaceId.None)
            placeId = null; // search in all places
        var searchResults = await new[] {
                Search.FindUserContacts(session, placeId, criteria, cancellationToken),
                Search.FindChatContacts(session, placeId, criteria, false, cancellationToken),
                Search.FindChatContacts(session, placeId, criteria, true, cancellationToken),
            }.CollectResults()
            .ConfigureAwait(false);
        return searchResults.Where(x => x.HasValue).SelectMany(x => x.Value).ToList();
    }
}
