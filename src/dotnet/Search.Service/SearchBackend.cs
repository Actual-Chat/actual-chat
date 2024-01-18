using ActualChat.Chat;
using ActualChat.Search.Db;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Search;

internal class SearchBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), ISearchBackend
{
    private ElasticsearchClient? _elastic;
    private IChatsBackend? _chatsBackend;

    private ElasticsearchClient Elastic => _elastic ??= Services.GetRequiredService<ElasticsearchClient>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
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

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var indexName = chat.ToIndexName();

        var searchResponse =
            await Elastic.SearchAsync<IndexedEntry>(s
                        => s.Index(indexName )
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
            => new (x.Source!.Id, SearchMatch.New(x.Source.Content));
    }

    // [CommandHandler]
    public virtual async Task OnBulkIndex(SearchBackend_BulkIndex command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        if (Computed.IsInvalidating())
            return;

        var updated = command.Updated.Require(IndexedEntry.MustBeValid).ToList();
        var deletedIds = command.Deleted.Select(lid => new Id(new TextEntryId(chatId, lid, AssumeValid.Option))).ToList();
        if (updated.Count == 0 && deletedIds.Count == 0)
            return;

        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var indexName = chat.ToIndexName();

        await Elastic
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(indexName))
                    .DeleteMany(deletedIds, (op, _) => op.Index(indexName)),
                cancellationToken)
            .Assert(Log)
            .ConfigureAwait(false);
        await Elastic.Indices.RefreshAsync(r => r.Indices(indexName), cancellationToken).ConfigureAwait(false);
    }
}
