using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatThreadsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> ListIds(ChatId parentChatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatThread> OnStart(ChatThreadsBackend_Start command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreadsBackend_Start(
    [property: DataMember, MemoryPackOrder(1)] ChatId ParentChatId,
    [property: DataMember, MemoryPackOrder(2)] string Title
) : IBackendCommand<ChatThread>, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ParentChatId;
}
