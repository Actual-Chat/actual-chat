using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for tracking user read positions in chats.
/// </summary>
public interface IChatPositionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatPosition> Get(UserId userId, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(ChatPositionsBackend_Set command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to set a user's read position in a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositionsBackend_Set(
    [property: DataMember, MemoryPackOrder(0), Key(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] ChatPositionKind Kind,
    [property: DataMember, MemoryPackOrder(3), Key(3)] ChatPosition Position,
    [property: DataMember, MemoryPackOrder(4), Key(4)] bool Force = false
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}
