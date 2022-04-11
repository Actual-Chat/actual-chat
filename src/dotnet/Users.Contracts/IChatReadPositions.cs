namespace ActualChat.Users;

public interface IChatReadPositions
{
    [ComputeMethod]
    Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task UpdateReadPosition(UpdateReadPositionCommand command, CancellationToken cancellationToken);

    public record UpdateReadPositionCommand(Session Session, string ChatId, long EntryId) : ISessionCommand<UserAvatar>;
}
