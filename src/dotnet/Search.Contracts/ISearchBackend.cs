using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Search;

public interface ISearchBackend : IComputeService, IBackendService
{
    // Non-compute methods

    Task<ContactSearchResultPage> FindContacts(
        UserId ownerId,
        ContactSearchQuery query,
        CancellationToken cancellationToken);

    Task<EntrySearchResultPage> FindEntries(
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
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnContactChangedEvent(ContactChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0)] bool RefreshUsers = false,
    [property: DataMember, MemoryPackOrder(1)] bool RefreshGroups = false,
    [property: DataMember, MemoryPackOrder(2)] bool RefreshPlaces = false,
    [property: DataMember, MemoryPackOrder(3)] bool RefreshEntries = false
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId> // Review
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => default;
}
