﻿using ActualLab.Rpc;
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
    Task OnUserContactBulkIndex(SearchBackend_UserContactBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnChatContactBulkIndex(SearchBackend_ChatContactBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPlaceContactBulkIndex(SearchBackend_PlaceContactBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStartPlaceContactIndexing(SearchBackend_StartPlaceContactIndexing command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStartUserContactIndexing(SearchBackend_StartUserContactIndexing command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStartChatContactIndexing(SearchBackend_StartChatContactIndexing command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnAccountChangedEvent(AccountChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnAuthorChangedEvent(AuthorChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_UserContactBulkIndex(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<IndexedUserContact> Updated,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedUserContact> Deleted
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Unit> // Review
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_ChatContactBulkIndex(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<IndexedGroupChatContact> Updated,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedGroupChatContact> Deleted
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Unit> // Review
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_PlaceContactBulkIndex(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<IndexedPlaceContact> Updated,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedPlaceContact> Deleted
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Unit> // Review
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_StartUserContactIndexing
    : ICommand<Unit>, IBackendCommand, IHasShardKey<Unit> // NOTE(AY): Will execute on a single backend now!
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_StartChatContactIndexing
    : ICommand<Unit>, IBackendCommand, IHasShardKey<Unit> // NOTE(AY): Will execute on a single backend now!
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_StartPlaceContactIndexing
    : ICommand<Unit>, IBackendCommand, IHasShardKey<Unit> // NOTE(AY): Will execute on a single backend now!
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
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
