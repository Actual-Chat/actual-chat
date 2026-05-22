using ActualLab.Rpc;

namespace ActualChat.Search;

/// <summary>
/// Backend service for full-text search across contacts and chat entries.
/// </summary>
public interface ISearchBackend : IComputeService, IBackendService
{
    // Non-compute methods

    Task<SearchResult<FoundContact>> FindContacts(
        UserId ownerId,
        ContactSearchQuery query,
        CancellationToken cancellationToken);

    Task<SearchResult<FoundChatEntry>> FindEntries(
        UserId userId,
        EntrySearchQuery query,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnAccountChangedEvent(AccountChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnPlaceMembershipChangedEvent(PlaceMembershipChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnContactChangedEvent(ContactChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to refresh the search index.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: MemoryPackConstructor, SerializationConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0), Key(0)] bool RefreshUsers = false,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool RefreshGroups = false,
    [property: DataMember, MemoryPackOrder(2), Key(2)] bool RefreshPlaces = false,
    [property: DataMember, MemoryPackOrder(3), Key(3)] bool RefreshEntries = false
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId?> // Review
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId? ShardKey => null;
}
