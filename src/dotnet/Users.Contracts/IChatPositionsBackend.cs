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
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatPositionsBackend_Set(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] ChatPositionKind Kind,
    [property: DataMember, Key(3)] ChatPosition Position,
    [property: DataMember, Key(4)] bool Force = false
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
