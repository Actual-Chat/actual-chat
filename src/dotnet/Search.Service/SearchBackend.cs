using ActualChat.Search.Db;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Search;

internal class SearchBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), ISearchBackend
{
    private ElasticsearchClient? _elastic;

    private ElasticsearchClient Elastic => _elastic ??= Services.GetRequiredService<ElasticsearchClient>();
    private ElasticConfigurator ElasticConfigurator { get; } = services.GetRequiredService<ElasticConfigurator>();

    // Not a [ComputeMethod]!
    public async Task<SearchResultPage> Search(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);
        skip = skip.Clamp(0, int.MaxValue);
        limit = limit.Clamp(0, Constants.Search.PageSizeLimit);

        var searchResponse =
            await Elastic.SearchAsync<IndexedEntry>(s
                        => s.Index(chatId.ToIndexName())
                            .From(skip)
                            .Size(limit)
                            .Query(q => q.MatchPhrasePrefix(p => p.Query(criteria).Field(x => x.Content))),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCallDetails.HttpStatusCode == StatusCodes.Status404NotFound)
            return SearchResultPage.Empty;

        return new SearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        EntrySearchResult ToSearchResult(Hit<IndexedEntry> x)
            => new (new TextEntryId(chatId, x.Source!.Id, AssumeValid.Option), SearchMatch.New(x.Source.Content));
    }

    // [CommandHandler]
    public virtual async Task OnBulkIndex(SearchBackend_BulkIndex command, CancellationToken cancellationToken)
    {
        var (chatId, updated, deleted) = command;
        if (Computed.IsInvalidating())
            return;

        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        await Elastic
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(chatId.ToIndexName()))
                    .DeleteMany(deleted.Select(lid => new Id(lid))),
                cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
        await Elastic.Indices.RefreshAsync(r => r.Indices(chatId.ToIndexName()), cancellationToken).ConfigureAwait(false);
    }
}
