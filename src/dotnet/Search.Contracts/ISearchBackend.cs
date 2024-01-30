using MemoryPack;

namespace ActualChat.Search;

public interface ISearchBackend : IComputeService
{
    [CommandHandler]
    Task OnEntryBulkIndex(SearchBackend_EntryBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUserContactBulkIndex(SearchBackend_UserContactBulkIndex command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnChatContactBulkIndex(SearchBackend_ChatContactBulkIndex command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken);

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
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_UserContactBulkIndex(
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedUserContact> Updated,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<IndexedUserContact> Deleted
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_ChatContactBulkIndex(
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedChatContact> Updated,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<IndexedChatContact> Deleted
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<ChatId> ChatIds,
    [property: DataMember, MemoryPackOrder(1)] bool RefreshUsers,
    [property: DataMember, MemoryPackOrder(2)] bool RefreshPublicChats,
    [property: DataMember, MemoryPackOrder(3)] bool RefreshPrivateChats
) : ICommand<Unit>, IBackendCommand
{
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
