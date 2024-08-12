using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Queues;
using ActualChat.Search.Db;
using ActualChat.Search.Module;
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
    private IPlacesBackend PlacesBackend { get; } = services.GetRequiredService<IPlacesBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private UserContactIndexer UserContactIndexer { get; } = services.GetRequiredService<UserContactIndexer>();
    private GroupChatContactIndexer GroupChatContactIndexer { get; } = services.GetRequiredService<GroupChatContactIndexer>();
    private PlaceContactIndexer PlaceContactIndexer { get; } = services.GetRequiredService<PlaceContactIndexer>();
    private OpenSearchConfigurator OpenSearchConfigurator { get; } = services.GetRequiredService<OpenSearchConfigurator>();
    private IQueues Queues { get; } = services.Queues();
    private ILogger? DebugLog => Constants.DebugMode.OpenSearchRequest ? Log : null;

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
            Log.LogWarning($"{nameof(FindPeople)}: search is disabled");
            return ContactSearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        query = query.Clamp();

        return query.Scope switch {
            ContactSearchScope.People => await FindPeople(ownerId, query, cancellationToken).ConfigureAwait(false),
            ContactSearchScope.Groups => await FindGroups(ownerId, query, cancellationToken).ConfigureAwait(false),
            ContactSearchScope.Places => await FindPlaces(ownerId, query, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Scope, "Contact search scope has unexpected value"),
        };
    }

    // Not a [ComputeMethod]!
    private async Task<ContactSearchResultPage> FindPeople(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownContactIds = await ContactsBackend
            .ListIdsForUserContactSearch(userId, cancellationToken)
            .ConfigureAwait(false);
        if (ownContactIds.IsEmpty && query.Own)
            return ContactSearchResultPage.Empty;

        var linkedUserIds = ownContactIds.Select(x => x.ChatId.PeerChatId.UserIds.OtherThan(userId)).ToList();
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedUserContact>(s
                        => s.Index(IndexNames.UserIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQueryDescriptor))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.FullName)))
                            .Log(OpenSearchClient, DebugLog, "People search request"),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedUserContact> ConfigureQueryDescriptor(
            BoolQueryDescriptor<IndexedUserContact> descriptor)
            => descriptor.Must(q
                        => q.MultiMatch(
                            m
                                => m.Fields(x => x.FullName, x => x.FirstName, x => x.SecondName)
                                    .Query(query.Criteria)
                                    .Type(TextQueryType.PhrasePrefix)
                                    .Operator(Operator.Or)
                                    .Slop(10)),
                    query.Own
                        ? q => q.Terms(m => m.Field(x => x.Id).Terms(linkedUserIds))
                        : null,
                    query.MustFilterByPlace
                        ? q => q.Match(m => m.Field(x => x.PlaceIds).Query(query.PlaceId.Value))
                        : null)
                .MustNot(q => q.Match(m => m.Field(x => x.Id).Query(userId)),
                    query.Own
                        ? null
                        : q => q.Terms(m => m.Field(x => x.Id).Terms(linkedUserIds)));

        ContactSearchResult ToSearchResult(IHit<IndexedUserContact> hit)
        {
            var peerChatId = new PeerChatId(userId, UserId.Parse(hit.Source!.Id));
            var contactId = new ContactId(userId, peerChatId.ToChatId());

            return new ContactSearchResult(contactId, hit.GetSearchMatch());
        }
    }

    // Not a [ComputeMethod]!
    private async Task<ContactSearchResultPage> FindGroups(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownGroupContactIds = await ContactsBackend.ListIdsForGroupContactSearch(userId, query.PlaceId, cancellationToken).ConfigureAwait(false);
        if (ownGroupContactIds.IsEmpty && query.Own)
            return ContactSearchResultPage.Empty;

        var ownGroupIds = ownGroupContactIds.Select(x => x.ChatId).ToList();
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedGroupChatContact>(s
                        => s.Index(IndexNames.GroupIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQueryDescriptor))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Title)))
                            .Log(OpenSearchClient, DebugLog, "Group search request"),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedGroupChatContact> ConfigureQueryDescriptor(BoolQueryDescriptor<IndexedGroupChatContact> descriptor)
            => descriptor
                .Must(q => q.MatchBoolPrefix(p => p.Query(query.Criteria).Field(x => x.Title).Operator(Operator.And)),
                    query.MustFilterByPlace
                        ? q => q.Term(t => t.Field(x => x.PlaceId).Value(query.PlaceId.Value))
                        : null,
                    query.Own
                        ? q => q.Terms(t => t.Field(x => x.Id).Terms(ownGroupIds))
                        : q => q.Term(t => t.Field(x => x.IsPublic).Value(true)))
                .MustNot(
                    query.Own
                        ? null
                        : q => q.Terms(t => t.Field(x => x.Id).Terms(ownGroupIds)));

        ContactSearchResult ToSearchResult(IHit<IndexedGroupChatContact> hit)
            => new (new ContactId(userId, ChatId.Parse(hit.Source!.Id)), hit.GetSearchMatch());
    }

    // Not a [ComputeMethod]!
    private async Task<ContactSearchResultPage> FindPlaces(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownPlaceIds = await ContactsBackend.ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        if (ownPlaceIds.IsEmpty && query.Own)
            return ContactSearchResultPage.Empty;

        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedPlaceContact>(s
                        => s.Index(IndexNames.PlaceIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQueryDescriptor))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Title)))
                            .Log(OpenSearchClient, DebugLog, "Place search request"),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;

        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedPlaceContact> ConfigureQueryDescriptor(
            BoolQueryDescriptor<IndexedPlaceContact> descriptor)
            => descriptor
                .Must(q => q.MatchBoolPrefix(p => p.Query(query.Criteria).Field(x => x.Title).Operator(Operator.And)),
                    query.Own
                        ? q => q.Terms(t => t.Field(x => x.Id).Terms(ownPlaceIds))
                        : q => q.Term(t => t.Field(x => x.IsPublic).Value(true)))
                .MustNot(query.Own
                    ? null
                    : q => q.Terms(t => t.Field(x => x.Id).Terms(ownPlaceIds)));

        ContactSearchResult ToSearchResult(IHit<IndexedPlaceContact> hit)
            => new (new ContactId(userId, PlaceId.Parse(hit.Source!.Id).ToRootChatId()), hit.GetSearchMatch());
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

        var updated = command.Updated;
        var deleted = command.Deleted;
        if (deleted.IsEmpty && updated.IsEmpty)
            return;

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        await IndexUserContacts(updated, deleted, cancellationToken).ConfigureAwait(false);
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
    public virtual async Task OnPlaceContactBulkIndex(SearchBackend_PlaceContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnPlaceContactBulkIndex)}: search is disabled");
            return;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        await IndexPlaceContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
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
            indices.Add(IndexNames.UserIndexName);
        if (command.RefreshGroups)
            indices.Add(IndexNames.GroupIndexName);
        if (command.RefreshPlaces)
            indices.Add(IndexNames.PlaceIndexName);
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

        GroupChatContactIndexer.OnSyncNeeded();
        return Task.CompletedTask;
    }

    // [CommandHandler]
    public virtual Task OnStartPlaceContactIndexing(
        SearchBackend_StartPlaceContactIndexing command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // it only notifies indexing job

        if (!Settings.IsSearchEnabled) {
            Log.LogWarning($"{nameof(OnStartPlaceContactIndexing)}: search is disabled");
            return Task.CompletedTask;
        }

        PlaceContactIndexer.OnSyncNeeded();
        return Task.CompletedTask;
    }

    // [EventHandler]
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

    // [EventHandler]
    public virtual async Task OnAuthorChangedEvent(AuthorChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsSearchEnabled)
            return;

        var (author, _) = eventCommand;
        Log.LogDebug("Received AuthorChangedEvent #{Id}", author.Id);
        await Queues.Enqueue(new SearchBackend_StartUserContactIndexing(), cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsSearchEnabled)
            return;

        var (chat, _, changeKind) = eventCommand;
        if (chat.Id.Kind == ChatKind.Peer || chat.Id.IsPlaceRootChat)
            return;

        // NOTE: we don't have any other chance to process removed items
        if (changeKind == ChangeKind.Remove) {
            var place = await PlacesBackend.Get(chat.Id.PlaceId, cancellationToken).ConfigureAwait(false);
            var deletedContacts = ApiArray.New(chat.ToIndexedChatContact(place));
            await Queues.Enqueue(new SearchBackend_ChatContactBulkIndex([], deletedContacts), cancellationToken).ConfigureAwait(false);
        }
        else
            await Queues.Enqueue(new SearchBackend_StartChatContactIndexing(), cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsSearchEnabled)
            return;

        var (place, _, changeKind) = eventCommand;
        // NOTE: we don't have any other chance to process removed items
        if (changeKind == ChangeKind.Remove) {
            var deletedContacts = ApiArray.New(place.ToIndexedPlaceContact());
            await Queues.Enqueue(new SearchBackend_PlaceContactBulkIndex([], deletedContacts), cancellationToken).ConfigureAwait(false);
        }
        else
            await Queues.Enqueue(new SearchBackend_StartPlaceContactIndexing(), cancellationToken).ConfigureAwait(false);
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
                    .IndexMany(updated, (op, _) => op.Index(IndexNames.UserIndexName))
                    .DeleteMany(deleted, (op, _) => op.Index(IndexNames.UserIndexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexChatContacts(
        IReadOnlyCollection<IndexedGroupChatContact> updated,
        IReadOnlyCollection<IndexedGroupChatContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r
                    => r.IndexMany(updated, (op, _) => op.Index(IndexNames.GroupIndexName))
                        .DeleteMany(deleted, (op, _) => op.Index(IndexNames.GroupIndexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexPlaceContacts(
        IReadOnlyCollection<IndexedPlaceContact> updated,
        IReadOnlyCollection<IndexedPlaceContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r
                    => r.IndexMany(updated, (op, _) => op.Index(IndexNames.PlaceIndexName))
                        .DeleteMany(deleted, (op, _) => op.Index(IndexNames.PlaceIndexName)),
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
                    .IgnoreUnavailable()
                    .Log(OpenSearchClient, DebugLog, "Entry search request"),
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
