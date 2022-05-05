namespace ActualChat.Users;

public interface IChatReadPositions
{
    [ComputeMethod]
    Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task UpdateReadPosition(UpdateReadPositionCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateReadPositionCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] long EntryId
        ) : ISessionCommand<UserAvatar>;
}
