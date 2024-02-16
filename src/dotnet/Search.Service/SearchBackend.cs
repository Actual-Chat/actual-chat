using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Search.Db;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.EntityFramework;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace ActualChat.Search;

public class SearchBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), ISearchBackend
{
    private const int MaxRefreshChatCount = 100;
    private ElasticNames? _elasticNames;
    private ElasticsearchClient? _elastic;
    private IChatsBackend? _chatsBackend;
    private IContactsBackend? _contactsBackend;

    private ElasticNames ElasticNames => _elasticNames ??= Services.GetRequiredService<ElasticNames>();
    private ElasticsearchClient Elastic => _elastic ??= Services.GetRequiredService<ElasticsearchClient>();
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IContactsBackend ContactsBackend => _contactsBackend ??= Services.GetRequiredService<IContactsBackend>();
    private ElasticConfigurator ElasticConfigurator { get; } = services.GetRequiredService<ElasticConfigurator>();

    // Not a [ComputeMethod]!
    public async Task<EntrySearchResultPage> FindEntriesInChat(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        return await FindEntries(ElasticNames.GetIndexName(chat),
            criteria,
            skip,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<EntrySearchResultPage> FindEntriesInAllChats(
        UserId userId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        var indices = await GetIndicesForEntrySearch(userId, cancellationToken).ConfigureAwait(false);
        return await FindEntries(indices.ToArray(),
                criteria,
                skip,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<ContactSearchResultPage> FindUserContacts(
        UserId userId,
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
            await Elastic.SearchAsync<IndexedUserContact>(s
                        => s.Index(ElasticNames.PublicUserIndexName)
                            .From(skip)
                            .Size(limit)
                            .Query(q => q.MultiMatch(
                                m => m.Fields(x => x.FullName, x => x.FirstName, x => x.SecondName)
                                    .Query(criteria)
                                    .Type(TextQueryType.PhrasePrefix)))
                            // .Query(q => q.MatchPhrasePrefix(p
                            //     => p.Query(criteria)
                            //         .Field(x => x.FullName)
                            //         .Field(x => x.FirstName)
                            //         .Field(x => x.SecondName)))
                            .Query(q
                                => q.Bool(b
                                    => b.Should(q2
                                        => q2.MatchPhrasePrefix(p
                                            => p.Field(f => f.FullName).Query(criteria)))))
                            .IgnoreUnavailable(),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCallDetails.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        ContactSearchResult ToSearchResult(Hit<IndexedUserContact> x)
        {
            // TODO: highlighting
            var peerChatId = new PeerChatId(userId, x.Source!.Id);
            var contactId = new ContactId(userId, peerChatId.ToChatId());
            return new ContactSearchResult(contactId, SearchMatch.New(x.Source.FullName));
        }
    }

    // Not a [ComputeMethod]!
    public async Task<ContactSearchResultPage> FindChatContacts(
        UserId userId,
        bool isPublic,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        skip = skip.Clamp(0, int.MaxValue);
        limit = limit.Clamp(0, Constants.Search.PageSizeLimit);

        var chatContactIds = !isPublic
            ? await ContactsBackend.ListIdsForContactSearch(userId, cancellationToken).ConfigureAwait(false)
            : ApiArray<ContactId>.Empty;
        var searchResponse =
            await Elastic.SearchAsync<IndexedChatContact>(s
                        => s.Index(ElasticNames.GetChatContactIndexName(isPublic))
                            .From(skip)
                            .Size(limit)
                            .Query(qq => qq.Bool(b => {
                                b.Must(q => q.MatchPhrasePrefix(p => p.Query(criteria).Field(x => x.Title)));
                                if (!isPublic) {
                                    var terms = new TermsQueryField(chatContactIds
                                        .Select(x => (FieldValue)x.ChatId.Value)
                                        .ToList());
                                    b.Filter(q => q.Terms(t => t.Field(x => x.Id).Terms(terms)));
                                }
                            }))
                            .IgnoreUnavailable(),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCallDetails.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        ContactSearchResult ToSearchResult(Hit<IndexedChatContact> x)
        {
            // TODO: highlighting
            return new ContactSearchResult(new ContactId(userId, x.Source!.Id), SearchMatch.New(x.Source.Title));
        }
    }

    // [CommandHandler]
    public virtual async Task OnEntryBulkIndex(SearchBackend_EntryBulkIndex command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId.Require();
        if (Computed.IsInvalidating())
            return;

        var entriesWithUnexpectedChat = command.Updated.Where(x => !x.ChatId.IsNone && x.ChatId != chatId).ToList();
        if (entriesWithUnexpectedChat.Count > 0)
            throw StandardError.Constraint($"All indexed entries must have chat #{chatId}");

        var updated = command.Updated
            .Require(IndexedEntry.MustBeValid)
            .ToList();
        if (updated.Count == 0 && command.Deleted.Count == 0)
            return;

        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var indexName = ElasticNames.GetIndexName(chat);

        await IndexEntries(updated, command.Deleted, indexName, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUserContactBulkIndex(SearchBackend_UserContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        await IndexUserContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnChatContactBulkIndex(SearchBackend_ChatContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        await IndexChatContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
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
        var indices = new List<IndexName>();
        if (command.RefreshUsers)
            indices.Add(ElasticNames.PublicUserIndexName);
        if (command.RefreshPublicChats)
            indices.Add(ElasticNames.GetChatContactIndexName(true));
        if (command.RefreshPrivateChats)
            indices.Add(ElasticNames.GetChatContactIndexName(false));
        indices.AddRange(chats.SkipNullItems().Select(ElasticNames.GetIndexName).Distinct());
        if (indices.Count == 0)
            return;

        await Elastic.Indices.RefreshAsync(r => r.Indices(indices.ToArray()), cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<ApiSet<string>> GetIndicesForEntrySearch(UserId userId, CancellationToken cancellationToken)
    {
        // public place chats are not returned since we use scoped indexes
        var contactIds = await ContactsBackend.ListIdsForEntrySearch(userId, cancellationToken).ConfigureAwait(false);
        var indices = new List<IndexName>(ElasticNames.GetPeerChatSearchIndexNamePatterns(userId));
        indices.AddRange(contactIds.Select(x => ElasticNames.GetIndexName(x.ChatId, false)));
        return indices.Select(x => x.ToString()).ToApiSet();
    }

    // Private methods

    private Task IndexEntries(
        IReadOnlyCollection<IndexedEntry> updated,
        IReadOnlyCollection<IndexedEntry> deleted,
        IndexName indexName,
        CancellationToken cancellationToken)
        => Elastic
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(indexName))
                    .DeleteMany(deleted, (op, _) => op.Index(indexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexUserContacts(
        IReadOnlyCollection<IndexedUserContact> updated,
        IReadOnlyCollection<IndexedUserContact> deleted,
        CancellationToken cancellationToken)
        => Elastic
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(ElasticNames.PublicUserIndexName))
                    .DeleteMany(deleted, (op, _) => op.Index(ElasticNames.PublicUserIndexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexChatContacts(
        IReadOnlyCollection<IndexedChatContact> updated,
        IReadOnlyCollection<IndexedChatContact> deleted,
        CancellationToken cancellationToken)
        => Elastic
            .BulkAsync(r
                    => r.IndexMany(updated, (op, x) => op.Index(ElasticNames.GetChatContactIndexName(x.IsPublic)))
                        .DeleteMany(deleted, (op, x) => op.Index(ElasticNames.GetChatContactIndexName(x.IsPublic))),
                cancellationToken)
            .Assert(Log);

    private async Task<EntrySearchResultPage> FindEntries(
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
            return EntrySearchResultPage.Empty;

        return new EntrySearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        EntrySearchResult ToSearchResult(Hit<IndexedEntry> x)
            => new (x.Source!.Id, SearchMatch.New(x.Source.Content));
    }
}
