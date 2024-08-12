using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IChatUsagesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> GetRecencyList(UserId userId, ChatUsageListKind kind, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnRegisterUsage(ChatUsagesBackend_RegisterUsage command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPurgeRecencyList(ChatUsagesBackend_PurgeRecencyList command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsagesBackend_RegisterUsage(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] ChatUsageListKind Kind,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] DateTime? AccessTime
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsagesBackend_PurgeRecencyList(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] ChatUsageListKind Kind,
    [property: DataMember, MemoryPackOrder(2)] int Size
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
