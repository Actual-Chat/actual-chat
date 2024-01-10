using MemoryPack;

namespace ActualChat.Search;

public interface ISearchBackend : IComputeService
{
    Task<SearchResultPage> Search(
        ChatId chatId,
        string criteria,
        int skip,
        int limit,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task OnBulkIndex(SearchBackend_BulkIndex command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SearchBackend_BulkIndex(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedEntry> Updated,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<long> Deleted
) : ICommand<Unit>, IBackendCommand;
