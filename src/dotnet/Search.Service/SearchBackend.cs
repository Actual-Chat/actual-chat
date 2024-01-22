using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Search.Db;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Search;

internal class SearchBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), ISearchBackend
{
    private const int MaxRefreshChatCount = 100;
    private ElasticsearchClient? _elastic;
    private IChatsBackend? _chatsBackend;
    private IContactsBackend? _contactsBackend;

    private ElasticsearchClient Elastic => _elastic ??= Services.GetRequiredService<ElasticsearchClient>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IContactsBackend ContactsBackend => _contactsBackend ??= Services.GetRequiredService<IContactsBackend>();
    private ElasticConfigurator ElasticConfigurator { get; } = services.GetRequiredService<ElasticConfigurator>();

    // Not a [ComputeMethod]!
    public async Task<SearchResultPage> Search(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        return await Search(chat.ToIndexName(),
            criteria,
            skip,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<SearchResultPage> Search(
        UserId userId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        var indices = await GetIndices(userId, cancellationToken).ConfigureAwait(false);
        return await Search(indices.ToArray(),
                criteria,
                skip,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnBulkIndex(SearchBackend_BulkIndex command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId.Require();
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
    }

    // [CommandHandler]
    public virtual async Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken)
    {
        var chatIds = command.ChatIds;
        if (chatIds.Count > MaxRefreshChatCount)
            throw StandardError.Internal("Max chat count to index is " + MaxRefreshChatCount);

        if (Computed.IsInvalidating())
            return;

        var chats = await chatIds.Select(x => ChatsBackend.Get(x, cancellationToken)).Collect().ConfigureAwait(false);
        var indices = chats.SkipNullItems().Select(x => x.ToIndexName()).Distinct().ToArray();
        await Elastic.Indices.RefreshAsync(r => r.Indices(indices), cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<ApiSet<string>> GetIndices(UserId userId, CancellationToken cancellationToken)
    {
        var placeIds = await ContactsBackend.ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        var privatePlaceGroupContactIds = await ListChatIds(userId,
                ChatKind.Place,
                PlaceId.Any,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        var privateGroupContactIds = await ListChatIds(userId,
                ChatKind.Group,
                PlaceId.None,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        var publicGroupContactIds = await ListChatIds(userId,
                ChatKind.Group,
                PlaceId.None,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        var indices = new List<IndexName>(ElasticExt.GetPeerChatSearchIndexNamePatterns(userId));
        indices.AddRange(privateGroupContactIds.Select(x => x.ToIndexName(false)));
        indices.AddRange(privatePlaceGroupContactIds.Select(x => x.ToIndexName(false)));
        indices.AddRange(publicGroupContactIds.Select(x => x.ToIndexName(false)));
        indices.AddRange(placeIds.Select(x => x.ToIndexName()));

        return indices.Select(x => x.ToString()).ToApiSet();
    }

    // TODO: handle cases when there are too many contacts for a user
    [ComputeMethod]
    protected virtual async Task<ApiArray<ChatId>> ListChatIds(
        UserId userId,
        ChatKind? chatKind,
        PlaceId placeId,
        bool? isPublic,
        CancellationToken cancellationToken)
    {
        var contactIds = await ContactsBackend.ListIds(
                userId,
                chatKind,
                placeId,
                cancellationToken)
            .ConfigureAwait(false);
        var chatIds = contactIds.Select(x => x.ChatId).ToApiArray();
        if (isPublic is null)
            return chatIds;

        var placeIds = chatIds.Where(x => x.IsPlaceChat).Select(x => x.PlaceId).Distinct().ToList();
        if (placeIds.Count == 0)
            return chatIds.ToApiArray();

        var publicPlaceChatIds = await placeIds
            .Select(x => ChatsBackend.GetPublicChatIdsFor(x, cancellationToken))
            .Collect()
            .Flatten()
            .ConfigureAwait(false);
        return isPublic.Value
            ? chatIds.Intersect(publicPlaceChatIds).ToApiArray()
            : chatIds.Except(publicPlaceChatIds).ToApiArray();
    }

    // Private methods

    private async Task<SearchResultPage> Search(
        Indices indices,
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
                        => s.Index(indices)
                            .From(skip)
                            .Size(limit)
                            .Query(q => q.MatchPhrasePrefix(p => p.Query(criteria).Field(x => x.Content)))
                            .IgnoreUnavailable(),
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
}
