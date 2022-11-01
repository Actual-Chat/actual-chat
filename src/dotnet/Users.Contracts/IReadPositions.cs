namespace ActualChat.Users;

public interface IReadPositions : IComputeService
{
    [ComputeMethod]
    Task<long?> GetOwn(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long ReadEntryId
        ) : ISessionCommand<Unit>;
}
