namespace ActualChat.Users;

public interface IReadPositionsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatPosition> Get(UserId userId, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetCommand(
        [property: DataMember] UserId UserId,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] ChatPositionKind Kind,
        [property: DataMember] ChatPosition Position,
        [property: DataMember] bool Force = false
        ) : ICommand<Unit>, IBackendCommand;
}
