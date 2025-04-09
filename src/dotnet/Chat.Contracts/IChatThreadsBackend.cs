using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatThreadsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> ListIds(ChatId parentChatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatThread> OnChange(ChatThreadsBackend_Change command, CancellationToken cancellationToken);

    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreadsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatThreadDiff> Change
) : ICommand<ChatThread>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}
