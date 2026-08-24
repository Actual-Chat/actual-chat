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

/// <summary>
/// Backend service implementation for AI-powered search using OpenSearch.
/// </summary>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SearchBackend(IServiceProvider services) : DbServiceBase<MLSearchDbContext>(services), ISearchBackend
{
    private MLSearchSettings Settings { get; } = services.GetRequiredService<MLSearchSettings>();
    private OpenSearchNames OpenSearchNames { get; } = services.GetRequiredService<OpenSearchNames>();
    private IOpenSearchClient OpenSearchClient { get; } = services.GetRequiredService<IOpenSearchClient>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private OpenSearchConfigurator OpenSearchConfigurator { get; } = services.GetRequiredService<OpenSearchConfigurator>();
    private IndexedDocuments IndexedDocuments { get; } = services.GetRequiredService<IndexedDocuments>();
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private ILogger? OpenSearchDebugLog => Constants.DebugMode.OpenSearchRequestResponse ? Log : null;

    // Not a [ComputeMethod]!
    public async Task<SearchResult<FoundContact>> FindContacts(
        UserId ownerId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(FindContacts)}: search is disabled");
            return SearchResult<FoundContact>.Empty;
        }

        if (!OpenSearchConfigurator.WhenReady.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenReady.ConfigureAwait(false);
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
    public async Task<SearchResult<FoundChatEntry>> FindEntries(
        UserId userId,
        EntrySearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled) {
            Log.LogWarning($"{nameof(FindEntries)}: search is disabled");
            return SearchResult<FoundChatEntry>.Empty;
        }

        if (!OpenSearchConfigurator.WhenReady.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenReady.ConfigureAwait(false);
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
                : ResumeIndexingFlow<AccountIndexingFlow>("", cancellationToken);
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
                await ResumeIndexingFlow<PlaceContactIndexingFlow>("", cancellationToken)
                    .ConfigureAwait(false);
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
        await StartIndexingEntries().ConfigureAwait(false);
        await UpdateIndexedChatContacts().ConfigureAwait(false);
        return;

        Task UpdateIndexedChatContacts() {
            if (chat.Id.Kind == ChatKind.Peer || chat.Id is PlaceChatId { IsRoot: true })
                return Task.CompletedTask;

            // NOTE: we don't have any other chance to process removed items
            if (changeKind == ChangeKind.Remove)
                return IndexedDocuments.SaveGroups([], [chat.Id], cancellationToken);

            return ResumeIndexingFlow<GroupIndexingFlow>("", cancellationToken);
        }

        Task StartIndexingEntries()
            => changeKind != ChangeKind.Create || chat.Id is PlaceChatId { IsRoot: true }
                ? Task.CompletedTask
                : ResumeIndexingFlow<EntryIndexingFlow>(chat.Id.Value, cancellationToken);
    }

    // [EventHandler]
    public virtual async Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        if (!Settings.IsEnabled)
            return;

        var (place, _, changeKind) = eventCommand;
        await UpdatedIndexedPlaces().ConfigureAwait(false);
        return;

        Task UpdatedIndexedPlaces()
        {
            // NOTE: we don't have any other chance to process removed items
            if (changeKind == ChangeKind.Remove)
                return IndexedDocuments.SavePlaces([], [place.Id], cancellationToken);

            return ResumeIndexingFlow<PlaceIndexingFlow>("", cancellationToken);
        }
    }

    // [EventHandler]
    public virtual Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        var entry = eventCommand.Entry;
        return UpdateIndexedEntries();

        Task UpdateIndexedEntries()
            => entry.IsSystemEntry
                ? Task.CompletedTask
                : ResumeIndexingFlow<EntryIndexingFlow>(entry.ChatId.Value, cancellationToken);
    }

    // [EventHandler]
    public virtual Task OnContactChangedEvent(ContactChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        var (contact, _, changeKind) = eventCommand;
        return UpdateIndexedUserContacts();

        Task UpdateIndexedUserContacts() {
            if (contact.Kind != ContactKind.User)
                return Task.CompletedTask;

            return changeKind is ChangeKind.Remove
                ? IndexedDocuments.SaveUserContacts([], [contact.Id], cancellationToken)
                : ResumeIndexingFlow<UserContactIndexingFlow>("", cancellationToken);
        }
    }

    // Private methods

    private async Task<SearchResult<FoundContact>> FindPeopleFromContacts(
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
                            .Highlight(h => h.Fields(
                                f => f.Field(x => x.Name).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag),
                                f => f.Field(x => x.ExternalContactName).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag)))
                            .Log(OpenSearchClient, OpenSearchDebugLog, "People", OpenSearchNames.UserIndexName),
                    cancellationToken)
                .Assert(Log)
                .Log(OpenSearchClient, OpenSearchDebugLog, "People", OpenSearchNames.UserIndexName)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return SearchResult<FoundContact>.Empty;
        return new SearchResult<FoundContact> {
            Items = searchResponse.Hits.Select(ToSearchResult).ToArray(),
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
            => qc.Match(m => m.Field(x => x.OwnerId).Query(userId.Value));

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
            => descriptor.MultiMatch(mm
                => mm.Fields(f => f.Field(x => x.Name).Field(x => x.ExternalContactName))
                    .Operator(Operator.Or)
                    .Query(query.Criteria)
                    .Type(TextQueryType.PhrasePrefix)
                    .Slop(20));

        FoundContact ToSearchResult(IHit<IndexedUserContact> hit)
            => new (hit.Source!.Id, hit.GetSearchMatch());
    }

    private async Task<SearchResult<FoundContact>> FindPeopleNotFromContacts(
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
                            .Highlight(h => h.Fields(f => f.Field(x => x.Name).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag)))
                            .Log(OpenSearchClient, OpenSearchDebugLog, "People", OpenSearchNames.UserIndexName),
                    cancellationToken)
                .Assert(Log)
                .Log(OpenSearchClient, OpenSearchDebugLog, "People", OpenSearchNames.UserIndexName)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return SearchResult<FoundContact>.Empty;
        return new SearchResult<FoundContact> {
            Items = searchResponse.Hits.Select(ToSearchResult).ToArray(),
            Offset = query.Skip,
        };

        BoolQueryDescriptor<IndexedUser> ConfigureQuery(BoolQueryDescriptor<IndexedUser> descriptor)
            => descriptor.Must(
                    qc => qc.HasRelationName<IndexedUser>(r => r.ContactToUser),
                    qc => qc.MatchPhrasePrefix(m => m.Field(x => x.Name)
                        .Query(query.Criteria)
                        .Slop(20)),
                    query.MustFilterByPlace
                        ? q => q.Match(m => m.Field(x => x.PlaceIds).Query(query.PlaceId.Value))
                        : null)
                .MustNot(
                    qc => qc.HasChild<IndexedUserContact>(c => c.Query(q => q.Match(m => m.Field(x => x.OwnerId).Query(userId.Value)))),
                    qc => qc.Match(m => m.Field(x => x.Id).Query(userId.Value)));

        FoundContact ToSearchResult(IHit<IndexedUser> hit)
            => new(ContactId.NewUser(userId, hit.Source.Id), hit.GetSearchMatch());
    }

    private async Task<SearchResult<FoundContact>> FindGroups(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var contactSubset = query.PlaceId is not null ? ContactSubset.Place(query.PlaceId) : ContactSubset.All();
        var ownGroupContactIds = await ContactsBackend.ListIdsForGroupContactSearch(userId, contactSubset, cancellationToken).ConfigureAwait(false);
        if (ownGroupContactIds.Length == 0 && query.Own)
            return SearchResult<FoundContact>.Empty;

        var ownGroupIds = ownGroupContactIds.Select(x => x.ChatId).ToList();
        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedGroup>(
                    s => s.Index(OpenSearchNames.GroupIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Title)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, OpenSearchDebugLog, "Group", OpenSearchNames.GroupIndexName),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return SearchResult<FoundContact>.Empty;
        return new SearchResult<FoundContact> {
            Items = searchResponse.Hits.Select(ToSearchResult).ToArray(),
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

        FoundContact ToSearchResult(IHit<IndexedGroup> hit)
            => new(ContactId.NewAny(userId, hit.Source.Id), hit.GetSearchMatch());
    }

    private async Task<SearchResult<FoundContact>> FindPlaces(
        UserId userId,
        ContactSearchQuery query,
        CancellationToken cancellationToken)
    {
        var ownPlaceIds = await ContactsBackend.ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        if (ownPlaceIds.Length == 0 && query.Own)
            return SearchResult<FoundContact>.Empty;

        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedPlace>(s
                        => s.Index(OpenSearchNames.PlaceIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(qq => qq.Bool(ConfigureQuery))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Title)).PreTags(HighlightsConverter.PreTag).PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, OpenSearchDebugLog, "Place", OpenSearchNames.PlaceIndexName),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return SearchResult<FoundContact>.Empty;

        return new SearchResult<FoundContact> {
            Items = searchResponse.Hits.Select(ToSearchResult).ToArray(),
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

        FoundContact ToSearchResult(IHit<IndexedPlace> hit)
            => new(ContactId.NewAny(userId, hit.Source.Id.RootChatId), hit.GetSearchMatch());
    }

    private async Task<SearchResult<FoundChatEntry>> FindEntriesInOpenSearch(
        UserId userId,
        EntrySearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!OpenSearchConfigurator.WhenReady.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenReady.ConfigureAwait(false);

        var chatIds = await ListChatIds().ConfigureAwait(false);
        // OpenSearch.Client omits a conditionless Terms clause, so ConfigureQuery needs a non-empty chatIds
        if (chatIds.Count == 0)
            return SearchResult<FoundChatEntry>.Empty;

        var hashtags = query.GetHashtags(MarkupParser);

        var searchResponse =
            await OpenSearchClient.SearchAsync<IndexedEntry>(searchDescriptor
                        => searchDescriptor.Index(OpenSearchNames.EntryIndexName)
                            .From(query.Skip)
                            .Size(query.Limit)
                            .Query(q => q.Bool(ConfigureQuery))
                            .Sort(s => s.Descending(x => x.At))
                            .IgnoreUnavailable()
                            .Highlight(h => h.Fields(f => f.Field(x => x.Content))
                                .PreTags(HighlightsConverter.PreTag)
                                .PostTags(HighlightsConverter.PostTag))
                            .Log(OpenSearchClient, OpenSearchDebugLog, "Entry", OpenSearchNames.EntryIndexName),
                    cancellationToken)
                .Assert(Log)
                .ConfigureAwait(false);
        if (searchResponse.ApiCall.HttpStatusCode == StatusCodes.Status404NotFound)
            return SearchResult<FoundChatEntry>.Empty;

        return new SearchResult<FoundChatEntry> {
            Items = searchResponse.Hits.Select(ToSearchResult).ToArray(),
            Offset = query.Skip,
        };

        FoundChatEntry ToSearchResult(IHit<IndexedEntry> hit)
            => new(hit.Source!.Id, hit.GetSearchMatch()) {
                HighlightedWords = hit.GetHighlightedWords().ToApiSet(StringComparer.OrdinalIgnoreCase),
            };

        BoolQueryDescriptor<IndexedEntry> ConfigureQuery(BoolQueryDescriptor<IndexedEntry> descriptor) {
            // The Content clause stays even when the criteria is nothing but a tag: the standard
            // analyzer drops the '#', so it still matches, and the entry's highlight is built from
            // it - a tags-only query would leave IHit.Highlight without a content field at all.
            var clauses = new List<Func<QueryContainerDescriptor<IndexedEntry>, QueryContainer>> {
                q => q.MatchBoolPrefix(p => p.Query(query.Criteria).Field(x => x.Content).Operator(Operator.And)),
                qc => qc.HasParent<IndexedChat>(
                    p => p.Query(q => q.Terms(t => t.Field(x => x.Id).Terms(chatIds)))),
            };
            foreach (var (tag, isPrefix) in hashtags)
                clauses.Add(isPrefix
                    ? q => q.Prefix(p => p.Field(x => x.Hashtags).Value(tag))
                    : q => q.Term(t => t.Field(x => x.Hashtags).Value(tag)));

            return descriptor.Must(clauses.ToArray());
        }

        async Task<List<ChatId>> ListChatIds() {
            if (query.ChatId is not null)
                return [query.ChatId];

            var contactSubset = query.PlaceId is not null ? ContactSubset.Place(query.PlaceId) : ContactSubset.All();
            var contactIds = await ContactsBackend
                .ListIdsForSearch(userId, contactSubset, true, cancellationToken)
                .ConfigureAwait(false);
            if (query.PlaceId is not { } placeId)
                return contactIds.Select(x => x.ChatId).ToList();

            // TODO: move this logic inside ListIdsForSearch
            var peerContactIds = await ContactsBackend.ListPeerContactIds(userId, placeId, cancellationToken).ConfigureAwait(false);
            return contactIds.Concat(peerContactIds).Select(x => x.ChatId).ToList();
        }
    }

    private Task ResumeIndexingFlow<TFlow>(
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => FlowHub.NewResumeEvent<TFlow>(arguments)
            .WithDelay(Settings.ChangedEntityIndexingDelay, Settings.IndexingFlowResumeDelayQuanta)
            .Schedule(cancellationToken);
}
