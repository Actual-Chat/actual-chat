using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using ActualLab.Fusion.EntityFramework;
using Microsoft.AspNetCore.Http;
using OpenSearch.Client;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

namespace ActualChat.MLSearch;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SearchBackend(IServiceProvider services) : DbServiceBase<MLSearchDbContext>(services), ISearchBackend
{
    private MLSearchSettings Settings { get; } = services.GetRequiredService<MLSearchSettings>();
    private OpenSearchNames OpenSearchNames { get; } = services.GetRequiredService<OpenSearchNames>();
    private IOpenSearchClient OpenSearchClient { get; } = services.GetRequiredService<IOpenSearchClient>();
    private IPlacesBackend PlacesBackend { get; } = services.GetRequiredService<IPlacesBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private UserContactIndexer UserContactIndexer { get; } = services.GetRequiredService<UserContactIndexer>();
    private GroupChatContactIndexer GroupChatContactIndexer { get; } = services.GetRequiredService<GroupChatContactIndexer>();
    private PlaceContactIndexer PlaceContactIndexer { get; } = services.GetRequiredService<PlaceContactIndexer>();
    private OpenSearchConfigurator OpenSearchConfigurator { get; } = services.GetRequiredService<OpenSearchConfigurator>();
    private IQueues Queues { get; } = services.Queues();
    private ILogger? DebugLog => Constants.DebugMode.OpenSearchRequest ? Log : null;

    // Not a [ComputeMethod]!
    public async Task<ContactSearchResultPage> FindContacts(
        UserId ownerId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(FindPeople)}: search is disabled");
            return ContactSearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);
        query = query.Clamp();
        return query.Scope switch {
            SearchScope.People => await FindPeople(ownerId, query, cancellationToken).ConfigureAwait(false),
            SearchScope.Groups => await FindGroups(ownerId, query, cancellationToken).ConfigureAwait(false),
            SearchScope.Places => await FindPlaces(ownerId, query, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Scope, "Contact search scope has unexpected value"),
        };
    }

    // Not a [ComputeMethod]!
    public async Task<EntrySearchResultPage> FindEntries(
        UserId userId,
        EntrySearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(FindEntries)}: search is disabled");
            return EntrySearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);
        query = query.Clamp();
        return await FindEntriesInOpenSearch(userId, query, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUserContactBulkIndex(SearchBackend_UserContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(OnUserContactBulkIndex)}: search is disabled");
            return;
        }

        var updated = command.Updated;
        var deleted = command.Deleted;
        if (deleted.IsEmpty && updated.IsEmpty)
            return;

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        Log.LogDebug("Indexing users: {UpdatedCount} updated and {DeletedCount} deleted", command.Updated.Count, command.Deleted.Count);
        await IndexUserContacts(updated, deleted, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnChatContactBulkIndex(SearchBackend_ChatContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(OnChatContactBulkIndex)}: search is disabled");
            return;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        Log.LogDebug("Indexing group chats: {UpdatedCount} updated and {DeletedCount} deleted", command.Updated.Count, command.Deleted.Count);
        await IndexChatContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnPlaceContactBulkIndex(SearchBackend_PlaceContactBulkIndex command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(OnPlaceContactBulkIndex)}: search is disabled");
            return;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        Log.LogDebug("Indexing places: {UpdatedCount} updated and {DeletedCount} deleted", command.Updated.Count, command.Deleted.Count);
        await IndexPlaceContacts(command.Updated, command.Deleted, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(OnRefresh)}: search is disabled");
            return;
        }

        var indices = new List<IndexName>();
        if (command.RefreshUsers)
            indices.Add(OpenSearchNames.UserIndexName);
        if (command.RefreshGroups)
            indices.Add(OpenSearchNames.GroupIndexName);
        if (command.RefreshPlaces)
            indices.Add(OpenSearchNames.PlaceIndexName);
        if (command.RefreshEntries)
            indices.Add(OpenSearchNames.EntryIndexName);
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

        if (!Settings.IsEnabled) {
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

        if (!Settings.IsEnabled) {
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

        if (!Settings.IsEnabled) {
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

        if (!Settings.IsEnabled)
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

        if (!Settings.IsEnabled)
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

        if (!Settings.IsEnabled)
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

        if (!Settings.IsEnabled)
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

    private Task IndexUserContacts(
        IReadOnlyCollection<IndexedUserContact> updated,
        IReadOnlyCollection<IndexedUserContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r => r
                    .IndexMany(updated, (op, _) => op.Index(OpenSearchNames.UserIndexName))
                    .DeleteMany(deleted, (op, _) => op.Index(OpenSearchNames.UserIndexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexChatContacts(
        IReadOnlyCollection<IndexedGroupChatContact> updated,
        IReadOnlyCollection<IndexedGroupChatContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r
                    => r.IndexMany(updated, (op, _) => op.Index(OpenSearchNames.GroupIndexName))
                        .DeleteMany(deleted, (op, _) => op.Index(OpenSearchNames.GroupIndexName)),
                cancellationToken)
            .Assert(Log);

    private Task IndexPlaceContacts(
        IReadOnlyCollection<IndexedPlaceContact> updated,
        IReadOnlyCollection<IndexedPlaceContact> deleted,
        CancellationToken cancellationToken)
        => OpenSearchClient
            .BulkAsync(r
                    => r.IndexMany(updated, (op, _) => op.Index(OpenSearchNames.PlaceIndexName))
                        .DeleteMany(deleted, (op, _) => op.Index(OpenSearchNames.PlaceIndexName)),
                cancellationToken)
            .Assert(Log);

    private async Task<ContactSearchResultPage> FindPeople(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownContactIds = await ContactsBackend
            .ListPeerContactIds(userId, cancellationToken)
            .ConfigureAwait(false);
        if (ownContactIds.IsEmpty && query.Own)
            return ContactSearchResultPage.Empty;

        var linkedUserIds = ownContactIds.Select(x => x.ChatId.PeerChatId.UserIds.OtherThan(userId)).ToList();
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedUserContact>(s
                        => s.Index(OpenSearchNames.UserIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
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

        BoolQueryDescriptor<IndexedUserContact> ConfigureQuery(
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
                        => s.Index(OpenSearchNames.GroupIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
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

        BoolQueryDescriptor<IndexedGroupChatContact> ConfigureQuery(BoolQueryDescriptor<IndexedGroupChatContact> descriptor)
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
                        => s.Index(OpenSearchNames.PlaceIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
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

        BoolQueryDescriptor<IndexedPlaceContact> ConfigureQuery(
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

    private async Task<EntrySearchResultPage> FindEntriesInOpenSearch(
        UserId userId,
        EntrySearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);
        var chatIds = await ListChatIds().ConfigureAwait(false);

        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedEntry>(searchDescriptor
                        => searchDescriptor.Index(OpenSearchNames.EntryIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(q => q.Bool(ConfigureQuery))
                            .Sort(s => s.Descending(x => x.At))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Content)))
                            .Log(OpenSearchClient, DebugLog, "Entry search request"),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return EntrySearchResultPage.Empty;

        return new EntrySearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        EntrySearchResult ToSearchResult(IHit<IndexedEntry> hit)
            => new (hit.Source!.Id, hit.GetSearchMatch());

        BoolQueryDescriptor<IndexedEntry> ConfigureQuery(BoolQueryDescriptor<IndexedEntry> descriptor)
            => descriptor.Must(q
                    => q.MatchBoolPrefix(p => p.Query(query.Criteria).Field(x => x.Content).Operator(Operator.And)),
                chatIds.Count > 0
                    ? qc => qc.HasParent<IndexedChat>(
                        p => p.Query(q => q.Terms(t => t.Field(x => x.Id).Terms(chatIds))))
                    : null);

        async Task<List<ChatId>> ListChatIds()
        {
            if (!query.ChatId.IsNone)
                return [query.ChatId];

            var contactIds = await ContactsBackend.ListIdsForSearch(userId, query.PlaceId, true, cancellationToken).ConfigureAwait(false);
            if (query.PlaceId is not { IsNone: false } placeId)
                return contactIds.Select(x => x.ChatId).ToList();

            // TODO: move this logic inside ListIdsForSearch
            var peerContactIds = await ContactsBackend.ListPeerContactIds(userId, placeId, cancellationToken).ConfigureAwait(false);
            return contactIds.Concat(peerContactIds).Select(x => x.ChatId).ToList();
        }
    }
}
