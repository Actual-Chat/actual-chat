using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record IndexedChatsBackend_BulkChange(
    [property: DataMember, MemoryPackOrder(1)] ApiArray<IndexedChatChange> Changes
) : ICommand<ApiArray<IndexedChat?>>, IBackendCommand;

public interface IIndexedChatsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<IndexedChat?> GetLast(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IndexedChat?> Get(ChatId chatId, CancellationToken cancellationToken);
    Task<ApiArray<IndexedChat>> List(
        Moment minCreatedAt,
        ChatId lastId,
        int limit,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<ApiArray<IndexedChat?>> OnBulkChange(
        IndexedChatsBackend_BulkChange command,
        CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedChatChange(
    [property: DataMember, MemoryPackOrder(1)] ChatId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<IndexedChat> Change
);
