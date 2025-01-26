using ActualChat.Contacts;
using ActualChat.Flows;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Flows;
using ActualChat.MLSearch.Module;
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
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private OpenSearchConfigurator OpenSearchConfigurator { get; } = services.GetRequiredService<OpenSearchConfigurator>();
    private IndexedDocuments IndexedDocuments { get; } = services.GetRequiredService<IndexedDocuments>();
    private IFlows Flows { get; } = services.GetRequiredService<IFlows>();
    private ILogger? DebugLog => Constants.DebugMode.OpenSearchRequest ? Log : null;

    // Not a [ComputeMethod]!
    public async Task<ContactSearchResultPage> FindContacts(
        UserId ownerId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(FindContacts)}: search is disabled");
            return ContactSearchResultPage.Empty;
        }

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);
        query = query.Clamp();
        return query.Scope switch {
            SearchScope.People => query.Own
                ? await FindPeopleFromContacts(ownerId, query, cancellationToken).ConfigureAwait(false)
                : await FindPeopleNotFromContacts(ownerId, query, cancellationToken).ConfigureAwait(false),
            SearchScope.Groups => await FindGroups(ownerId, query, cancellationToken).ConfigureAwait(false),
            SearchScope.Places => await FindPlaces(ownerId, query, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(query),
                query.Scope,
                "Contact search scope has unexpected value"),
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

    // [EventHandler]
    public virtual Task OnAccountChangedEvent(AccountChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        if (!Settings.IsEnabled)
            return Task.CompletedTask;

        var (_, old, changeKind) = eventCommand;
        return UpdateIndexedUsers();

        Task UpdateIndexedUsers()
            // NOTE: we don't have any other chance to process removed items
            => changeKind == ChangeKind.Remove
                ? IndexedDocuments.SaveUsers([], [old!.Id], cancellationToken)
                : ResumeIndexingFlow<AccountIndexingFlow>("", "Account changed", cancellationToken);
    }

    // [EventHandler]
    public virtual Task OnPlaceMembershipChangedEvent(PlaceMembershipChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        if (!Settings.IsEnabled)
            return Task.CompletedTask;

        return UpdateIndexedUsers();

        async Task UpdateIndexedUsers()
        {
            if (!eventCommand.HasLeft) {
                await ResumeIndexingFlow<PlaceContactIndexingFlow>("", "Place membership changed", cancellationToken);
                return;
            }

            var ownerId = eventCommand.UserId;
            var placeIds = await ContactsBackend.ListPlaceIds(ownerId, cancellationToken).ConfigureAwait(false);
            await IndexedDocuments.UpsertPartially<IndexedUser, IIndexedUserUpsertForPlacesOnly, UserId>(
                    x => x.UserIndexName,
                    [IndexedUser.ForPartialPlacesUpsert(ownerId, placeIds)],
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsEnabled)
            return;

        var (chat, _, changeKind) = eventCommand;
        // TODO: Stop indexing removed chats
        await StartIndexingEntries();
        await UpdateIndexedChatContacts().ConfigureAwait(false);
        return;

        Task UpdateIndexedChatContacts()
        {
            if (chat.Id.Kind == ChatKind.Peer || chat.Id.IsPlaceRootChat)
                return Task.CompletedTask;

            // NOTE: we don't have any other chance to process removed items
            if (changeKind == ChangeKind.Remove)
                return IndexedDocuments.SaveGroups([], [chat.Id], cancellationToken);

            return ResumeIndexingFlow<GroupIndexingFlow>("", "Chat changed", cancellationToken);
        }

        Task StartIndexingEntries()
            => chat.Id.IsPlaceRootChat || changeKind != ChangeKind.Create
                ? Task.CompletedTask
                : Flows.GetOrStart<EntryIndexingFlow>(chat.Id, cancellationToken);
    }

    // [EventHandler]
    public virtual async Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsEnabled)
            return;

        var (place, _, changeKind) = eventCommand;
        await UpdatedIndexedPlaces();
        return;

        Task UpdatedIndexedPlaces()
        {
            // NOTE: we don't have any other chance to process removed items
            if (changeKind == ChangeKind.Remove)
                return IndexedDocuments.SavePlaces([], [place.Id], cancellationToken);

            return ResumeIndexingFlow<PlaceIndexingFlow>("", "Place changed", cancellationToken);
        }
    }

    // [EventHandler]
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        var entry = eventCommand.Entry;
        return UpdateIndexedEntries();

        Task UpdateIndexedEntries()
            => entry.IsSystemEntry || entry.Kind != ChatEntryKind.Text
                ? Task.CompletedTask
                : ResumeIndexingFlow<EntryIndexingFlow>(entry.ChatId, "TextEntry changed", cancellationToken);
    }

    // Private methods

    private async Task<ContactSearchResultPage> FindPeopleFromContacts(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedUserContact>(s
                        => s.Index(OpenSearchNames.UserIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Name)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, DebugLog, "People", OpenSearchNames.UserIndexName),
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
            => descriptor
                .Must(
                    FilterByOwner,
                    query.MustFilterByPlace ? FilterByPlace : null,
                    FilterByName);

        QueryContainer FilterByPlace(QueryContainerDescriptor<IndexedUserContact> descriptor)
            => descriptor.HasParent<IndexedUser>(p => p.Query(q => q.Match(m => m.Field(x => x.PlaceIds).Query(query.PlaceId.Value))));

        QueryContainer FilterByOwner(QueryContainerDescriptor<IndexedUserContact> qc)
            => qc.Match(m => m.Field(x => x.OwnerId).Query(userId));

        QueryContainer FilterByName(QueryContainerDescriptor<IndexedUserContact> descriptor)
            => descriptor.Bool(b
                => b.Should(FilterByContactName, FilterByAccountName).MinimumShouldMatch(MinimumShouldMatch.Fixed(1)));

        QueryContainer FilterByAccountName(QueryContainerDescriptor<IndexedUserContact> descriptor)
            => descriptor.HasParent<IndexedUser>(p
                => p.Query(qc => qc.MatchPhrasePrefix(m => m.Field(x => x.Name).Query(query.Criteria).Slop(20)))
                    .InnerHits(i
                        => i.Highlight(h
                            => h.Fields(f => f.Field(x => x.Name))
                                .PreTags(HighlightsConverter.PreTag)
                                .PostTags(HighlightsConverter.PostTag))));

        QueryContainer FilterByContactName(QueryContainerDescriptor<IndexedUserContact> descriptor)
            // TODO: boost for user contact peer name
            => descriptor.MatchPhrasePrefix(m => m.Field(x => x.Name).Query(query.Criteria).Slop(20));

        ContactSearchResult ToSearchResult(IHit<IndexedUserContact> hit)
            => new (hit.Source!.Id, hit.GetSearchMatch());
    }

    private async Task<ContactSearchResultPage> FindPeopleNotFromContacts(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedUser>(s
                        => s.Index(OpenSearchNames.UserIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Name)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, DebugLog, "People", OpenSearchNames.UserIndexName),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedUser> ConfigureQuery(BoolQueryDescriptor<IndexedUser> descriptor)
            => descriptor.Must(
                    qc => qc.HasRelationName<IndexedUser>(r => r.ContactToUser),
                    qc => qc.MatchPhrasePrefix(
                        m => m.Field(x => x.Name)
                            .Query(query.Criteria)
                            .Slop(20)),
                    query.MustFilterByPlace
                        ? q => q.Match(m => m.Field(x => x.PlaceIds).Query(query.PlaceId.Value))
                        : null)
                .MustNot(qc => qc.HasChild<IndexedUserContact>(c => c.Query(q => q.MatchAll())));

        ContactSearchResult ToSearchResult(IHit<IndexedUser> hit)
        {
            var peerChatId = new PeerChatId(userId, hit.Source!.Id);
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
            await OpenSearchClient.SearchAsync<IndexedGroup>(s
                        => s.Index(OpenSearchNames.GroupIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Title)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, DebugLog, "Group", OpenSearchNames.GroupIndexName),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;
        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedGroup> ConfigureQuery(BoolQueryDescriptor<IndexedGroup> descriptor)
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

        ContactSearchResult ToSearchResult(IHit<IndexedGroup> hit)
            => new (new ContactId(userId, hit.Source!.Id), hit.GetSearchMatch());
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
            await OpenSearchClient.SearchAsync<IndexedPlace>(s
                        => s.Index(OpenSearchNames.PlaceIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Title)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, DebugLog, "Place", OpenSearchNames.PlaceIndexName),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return ContactSearchResultPage.Empty;

        return new ContactSearchResultPage {
            Hits = searchResponse.Hits.Select(ToSearchResult).ToApiArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedPlace> ConfigureQuery(
            BoolQueryDescriptor<IndexedPlace> descriptor)
            => descriptor
                .Must(q => q.MatchBoolPrefix(p => p.Query(query.Criteria).Field(x => x.Title).Operator(Operator.And)),
                    query.Own
                        ? q => q.Terms(t => t.Field(x => x.Id).Terms(ownPlaceIds))
                        : q => q.Term(t => t.Field(x => x.IsPublic).Value(true)))
                .MustNot(query.Own
                    ? null
                    : q => q.Terms(t => t.Field(x => x.Id).Terms(ownPlaceIds)));

        ContactSearchResult ToSearchResult(IHit<IndexedPlace> hit)
            => new (new ContactId(userId, hit.Source!.Id.ToRootChatId()), hit.GetSearchMatch());
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
                            .Highlight(h => h.Fields(f => f.Field(x => x.Content)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, DebugLog, "Entry", OpenSearchNames.EntryIndexName),
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
            => new (hit.Source!.Id, hit.GetSearchMatch()) {
                HighlightedWords = hit.GetHighlightedWords().ToApiSet(StringComparer.OrdinalIgnoreCase),
            };

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

    private Task<TFlow?> ResumeIndexingFlow<TFlow>(
        string arguments,
        string? tag = null,
        CancellationToken cancellationToken = default) where TFlow : Flow
        => Flows.GetAndResume<TFlow>(arguments,
            Settings.ChangedEntityIndexingDelay,
            tag,
            Settings.ChangedEntityIndexingDelay + Settings.IndexingFlowResumeDelay, // to avoid too many flow executions
            cancellationToken);
}
