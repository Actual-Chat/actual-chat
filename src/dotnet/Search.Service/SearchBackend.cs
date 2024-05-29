using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Contacts;
using ActualChat.Queues;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
using ActualChat.Users.Events;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.EntityFramework;
using OpenSearch.Client;

namespace ActualChat.Search;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SearchBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), ISearchBackend
{
    private const int MaxRefreshChatCount = 100;

    private SearchSettings Settings { get; } = services.GetRequiredService<SearchSettings>();
    private IndexNames IndexNames { get; } = services.GetRequiredService<IndexNames>();
    private IOpenSearchClient OpenSearchClient { get; } = services.GetRequiredService<IOpenSearchClient>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private UserContactIndexer UserContactIndexer { get; } = services.GetRequiredService<UserContactIndexer>();
    private ChatContactIndexer ChatContactIndexer { get; } = services.GetRequiredService<ChatContactIndexer>();
    private OpenSearchConfigurator OpenSearchConfigurator { get; } = services.GetRequiredService<OpenSearchConfigurator>();
    private IQueues Queues { get; } = services.Queues();

    [ComputeMethod]
    protected virtual async Task<ApiSet<string>> GetIndicesForEntrySearch(UserId userId, CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(GetIndicesForEntrySearch)}: search is disabled");
            return ApiSet<string>.Empty;
        }

        // public place chats are not returned since we use scoped indexes
        var contactIds = await ContactsBackend.ListIdsForEntrySearch(userId, cancellationToken).ConfigureAwait(false);
        var indices = new List<IndexName>(IndexNames.GetPeerChatSearchIndexNamePatterns(userId));
        indices.AddRange(contactIds.Select(x => IndexNames.GetIndexName(x.ChatId, false)));
        return indices.Select(x => x.ToString()).ToApiSet();
    }

    // Non-compute methods

    // Not a [ComputeMethod]!
    public async Task<EntrySearchResultPage> FindEntriesInChat(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(FindEntriesInChat)}: search is disabled");
            return EntrySearchResultPage.Empty;
        }

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        return await FindEntries(IndexNames.GetIndexName(chat),
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
        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(FindEntriesInAllChats)}: search is disabled");
            return EntrySearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        var indices = await GetIndicesForEntrySearch(userId, cancellationToken).ConfigureAwait(false);
        return await FindEntries(indices.ToArray(),
                criteria,
                skip,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<ContactSearchResultPage> FindContacts(
        UserId ownerId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(FindUserContacts)}: search is disabled");
            return ContactSearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        var kind = query.Kind;
        switch (kind) {
        case ContactKind.User:
            return await FindUserContacts(ownerId, query, cancellationToken).ConfigureAwait(false);
        case ContactKind.Chat:
            return await FindChatContacts(ownerId, query, cancellationToken).ConfigureAwait(false);
        default:
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Invalid contact kind");
        }
    }

    // Not a [ComputeMethod]!
    private async Task<ContactSearchResultPage> FindUserContacts(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(FindUserContacts)}: search is disabled");
            return ContactSearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        // TODO: consider placeId
        var skip = query.Skip.Clamp(0, int.MaxValue);
        var limit = query.Limit.Clamp(0, Constants.Search.PageSizeLimit);

        ApiArray<UserId>? placeUserIds = null;
        if (query.PlaceId is { IsNone: false } placeId)
            placeUserIds = await AuthorsBackend.ListPlaceUserIds(placeId, cancellationToken).ConfigureAwait(false);

        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedUserContact>(s
                        => s.Index(IndexNames.PublicUserIndexName)
                            .From(skip)
                            .Size(limit)
                            .Query(qq => qq.Bool(ConfigureUserContactQueryDescriptor))
                            .IgnoreUnavailable(),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        ContactSearchResult ToSearchResult(IHit<IndexedUserContact> x)
        {
            // TODO: highlighting
            var peerChatId = new PeerChatId(userId, UserId.Parse(x.Source!.Id));
            var contactId = new ContactId(userId, peerChatId.ToChatId());
            return new ContactSearchResult(contactId, SearchMatch.New(x.Source.FullName));
        }

        BoolQueryDescriptor<IndexedUserContact> ConfigureUserContactQueryDescriptor(BoolQueryDescriptor<IndexedUserContact> descriptor)
        {
            descriptor = descriptor.Must(q
                    => q.MultiMatch(
                        m
                            => m.Fields(x => x.FullName, x => x.FirstName, x => x.SecondName)
                                .Query(query.Criteria)
                                .Type(TextQueryType.PhrasePrefix)))
                .MustNot(q => q.Match(m
                    => m.Field(x => x.Id).Query(userId)));
            if (placeUserIds == null)
                return descriptor;

            // TODO: we need to index place contacts since place can grow
            return descriptor.Filter(q => q.Terms(t => t.Field(x => x.Id).Terms(placeUserIds.Value)));
        }
    }

    // Not a [ComputeMethod]!
    private async Task<ContactSearchResultPage> FindChatContacts(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(FindChatContacts)}: search is disabled");
            return ContactSearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        var skip = query.Skip.Clamp(0, int.MaxValue);
        var limit = query.Limit.Clamp(0, Constants.Search.PageSizeLimit);

        var chatContactIds = !query.IsPublic
            ? await ContactsBackend.ListIdsForContactSearch(userId, query.PlaceId, cancellationToken).ConfigureAwait(false)
            : ApiArray<ContactId>.Empty;
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedChatContact>(s
                        => s.Index(IndexNames.GetChatContactIndexName(query.IsPublic))
                            .From(skip)
                            .Size(limit)
                            .Query(qq => qq.Bool(ConfigureChatContactQueryDescriptor))
                            .IgnoreUnavailable(),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        ContactSearchResult ToSearchResult(IHit<IndexedChatContact> x)
        {
            // TODO: highlighting
            return new ContactSearchResult(new ContactId(userId, ChatId.Parse(x.Source!.Id)), SearchMatch.New(x.Source.Title));
        }

        BoolQueryDescriptor<IndexedChatContact> ConfigureChatContactQueryDescriptor(BoolQueryDescriptor<IndexedChatContact> descriptor)
        {
            descriptor = descriptor.Must(q
                => q.MatchPhrasePrefix(p => p.Query(query.Criteria).Field(x => x.Title)));
            var chatIdTerms = chatContactIds
                .Select(x => x.ChatId.Value)
                .ToList();
            // filter private chats by ids
            if (!query.IsPublic)
                return descriptor.Filter(q => q.Terms(t => t.Field(x => x.Id).Terms(chatIdTerms)));

            // return all public chats
            if (query.PlaceId is null)
                return descriptor;

            // return public chats without places
            if (query.PlaceId == PlaceId.None)
                return descriptor.Filter(qq => qq.Term(t => t.Field(x => x.PlaceId).Value("").Verbatim()));

            // filter public chats by place id
            return descriptor.Filter(q => q.Term(t => t.Field(x => x.PlaceId).Value(query.PlaceId)));
        }
    }

    // [CommandHandler]
    public virtual async Task OnEntryBulkIndex(SearchBackend_EntryBulkIndex command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId.Require();
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnEntryBulkIndex)}: search is disabled");
            return;
        }

        var entriesWithUnexpectedChat = command.Updated.Where(x => !x.ChatId.IsNone && x.ChatId != chatId).ToList();
        if (entriesWithUnexpectedChat.Count > 0)
            throw StandardError.Constraint($"All indexed entries must have chat #{chatId}");

        var updated = command.Updated
            .Require(IndexedEntry.MustBeValid)
            .ToList();
        if (updated.Count == 0 && command.Deleted.Count == 0)
            return;

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var indexName = IndexNames.GetIndexName(chat);

        await IndexEntries(updated, command.Deleted, indexName, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUserContactBulkIndex(SearchBackend_UserContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnUserContactBulkIndex)}: search is disabled");
            return;
        }

        if (command.Deleted.IsEmpty && command.Updated.IsEmpty)
            return;

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        await IndexUserContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnChatContactBulkIndex(SearchBackend_ChatContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnChatContactBulkIndex)}: search is disabled");
            return;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        await IndexChatContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken)
    {
        var chatIds = command.ChatIds;
        if (chatIds.Count > MaxRefreshChatCount)
            throw StandardError.Internal("Max chat count to index is " + MaxRefreshChatCount);

        if (Invalidation.IsActive)
            return;

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnRefresh)}: search is disabled");
            return;
        }

        var chats = await chatIds.Select(x => ChatsBackend.Get(x, cancellationToken)).Collect().ConfigureAwait(false);
        var indices = new List<IndexName>();
        if (command.RefreshUsers)
            indices.Add(IndexNames.PublicUserIndexName);
        if (command.RefreshPublicChats)
            indices.Add(IndexNames.GetChatContactIndexName(true));
        if (command.RefreshPrivateChats)
            indices.Add(IndexNames.GetChatContactIndexName(false));
        indices.AddRange(chats.SkipNullItems().Select(IndexNames.GetIndexName).Distinct());
        if (indices.Count == 0)
            return;

        await OpenSearchClient.Indices.RefreshAsync(Indices.Index(indices), ct: cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual Task OnStartUserContactIndexing(
        SearchBackend_StartUserContactIndexing command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // it only notifies indexing job

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnStartUserContactIndexing)}: search is disabled");
            return Task.CompletedTask;
        }

        UserContactIndexer.OnSyncNeeded();
        return Task.CompletedTask;
    }

    // [CommandHandler]
    public virtual Task OnStartChatContactIndexing(
        SearchBackend_StartChatContactIndexing command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // it only notifies indexing job

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnStartChatContactIndexing)}: search is disabled");
            return Task.CompletedTask;
        }

        ChatContactIndexer.OnSyncNeeded();
        return Task.CompletedTask;
    }

    [EventHandler]
    public virtual async Task OnAccountChangedEvent(AccountChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsSearchEnabled)
            return;

        var (account, _, changeKind) = eventCommand;
        // NOTE: we don't have any other chance to process removed items
        Log.LogDebug("Received AccountChangedEvent {ChangeKind} #{Id}", changeKind, account.Id);
        if (changeKind == ChangeKind.Remove) {
            var deletedContacts = ApiArray.New(account.ToIndexedUserContact());
            await Queues.Enqueue(new SearchBackend_UserContactBulkIndex([], deletedContacts), cancellationToken).ConfigureAwait(false);
        }
        else
            await Queues.Enqueue(new SearchBackend_StartUserContactIndexing(), cancellationToken).ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsSearchEnabled)
            return;

        var (chat, _, changeKind) = eventCommand;
        // NOTE: we don't have any other chance to process removed items
        if (changeKind == ChangeKind.Remove) {
            var place = await ChatsBackend.GetPlace(chat.Id.PlaceId, cancellationToken).ConfigureAwait(false);
            var deletedContacts = ApiArray.New(chat.ToIndexedChatContact(place));
            await Queues.Enqueue(new SearchBackend_ChatContactBulkIndex([], deletedContacts), cancellationToken).ConfigureAwait(false);
        }
        else
            await Queues.Enqueue(new SearchBackend_StartChatContactIndexing(), cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private Task IndexEntries(
        IReadOnlyCollection<IndexedEntry> updated,
        IReadOnlyCollection<IndexedEntry> deleted,
        IndexName indexName,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(indexName))
                    .DeleteMany(deleted, (op, _) => op.Index(indexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexUserContacts(
        IReadOnlyCollection<IndexedUserContact> updated,
        IReadOnlyCollection<IndexedUserContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(IndexNames.PublicUserIndexName))
                    .DeleteMany(deleted, (op, _) => op.Index(IndexNames.PublicUserIndexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexChatContacts(
        IReadOnlyCollection<IndexedChatContact> updated,
        IReadOnlyCollection<IndexedChatContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r
                    => r.IndexMany(updated, (op, x) => op.Index(IndexNames.GetChatContactIndexName(x.IsPublic)))
                        .DeleteMany(deleted, (op, x) => op.Index(IndexNames.GetChatContactIndexName(x.IsPublic))),
                cancellationToken)
            .Assert(Log);

    private async Task<EntrySearchResultPage> FindEntries(
        Indices indices,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);
        skip = skip.Clamp(0, int.MaxValue);
        limit = limit.Clamp(0, Constants.Search.PageSizeLimit);

        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedEntry>(s
                => s.Index(indices)
                    .From(skip)
                    .Size(limit)
                    .Query(q => q.MatchPhrasePrefix(p => p.Query(criteria).Field(x => x.Content)))
                    .IgnoreUnavailable(),
                cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return EntrySearchResultPage.Empty;

        return new EntrySearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = skip,
        };

        EntrySearchResult ToSearchResult(IHit<IndexedEntry> x)
            => new (x.Source!.Id, SearchMatch.New(x.Source.Content));
    }
}
