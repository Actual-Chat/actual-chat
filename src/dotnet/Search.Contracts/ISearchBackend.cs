using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Search;

public interface ISearchBackend : IComputeService, IBackendService
{
    [CommandHandler]
    Task OnEntryBulkIndex(SearchBackend_EntryBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUserContactBulkIndex(SearchBackend_UserContactBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnChatContactBulkIndex(SearchBackend_ChatContactBulkIndex command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStartUserContactIndexing(SearchBackend_StartUserContactIndexing command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStartChatContactIndexing(SearchBackend_StartChatContactIndexing command, CancellationToken cancellationToken);

    // Non-compute methods
    Task<EntrySearchResultPage> FindEntriesInChat(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<EntrySearchResultPage> FindEntriesInAllChats(
        UserId userId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<ContactSearchResultPage> FindUserContacts(
        UserId userId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<ContactSearchResultPage> FindChatContacts(
        UserId userId,
        bool isPublic,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_EntryBulkIndex(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedEntry> Updated,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<IndexedEntry> Deleted
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
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
    [property: DataMember, MemoryPackOrder(0)] ApiArray<IndexedChatContact> Updated,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedChatContact> Deleted
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
[method: MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<ChatId> ChatIds,
    [property: DataMember, MemoryPackOrder(1)] bool RefreshUsers,
    [property: DataMember, MemoryPackOrder(2)] bool RefreshPublicChats,
    [property: DataMember, MemoryPackOrder(3)] bool RefreshPrivateChats
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId> // Review
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => !ChatIds.IsEmpty ? ChatIds[0] : default;

    public SearchBackend_Refresh(params ChatId[] chatIds) : this(chatIds.ToApiArray(), false, false, false) { }

    public SearchBackend_Refresh(
        bool refreshUsers = false,
        bool refreshPublicChats = false,
        bool refreshPrivateChats = false) : this(ApiArray<ChatId>.Empty,
        refreshUsers,
        refreshPublicChats,
        refreshPrivateChats)
    { }
}
