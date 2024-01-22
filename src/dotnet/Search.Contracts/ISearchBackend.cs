using MemoryPack;

namespace ActualChat.Search;

public interface ISearchBackend : IComputeService
{
    [CommandHandler]
    Task OnBulkIndex(SearchBackend_BulkIndex command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRefresh(SearchBackend_Refresh command, CancellationToken cancellationToken);

    // Non-compute methods
    Task<SearchResultPage> Search(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    Task<SearchResultPage> Search(
        UserId userId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_BulkIndex(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedEntry> Updated,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<long> Deleted
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0)]
    ApiArray<ChatId> ChatIds
) : ICommand<Unit>, IBackendCommand
{
    public SearchBackend_Refresh(params ChatId[] chatIds) : this(chatIds.ToApiArray()) { }
}
