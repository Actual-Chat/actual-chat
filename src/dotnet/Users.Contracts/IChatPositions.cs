namespace ActualChat.Users;

public interface IChatPositions : IComputeService
{
    [ComputeMethod]
    Task<ChatPosition> GetOwn(Session session, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] ChatPositionKind Kind,
        [property: DataMember] ChatPosition Position
        ) : ISessionCommand<Unit>;
}
