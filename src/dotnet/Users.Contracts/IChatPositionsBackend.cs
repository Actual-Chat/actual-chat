using MemoryPack;

namespace ActualChat.Users;

public interface IChatPositionsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatPosition> Get(UserId userId, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(ChatPositionsBackend_Set command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositionsBackend_Set(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] ChatPositionKind Kind,
    [property: DataMember, MemoryPackOrder(3)] ChatPosition Position,
    [property: DataMember, MemoryPackOrder(4)] bool Force = false
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
