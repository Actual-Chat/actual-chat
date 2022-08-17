namespace ActualChat.Users;

public interface IChatReadPositions : IComputeService
{
    [ComputeMethod]
    Task<long?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetReadPositionCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetReadPositionCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long LastReadEntryId
        ) : ISessionCommand<UserAvatar>;
}
