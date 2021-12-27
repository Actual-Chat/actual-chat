namespace ActualChat.Users;

public interface IChatUserSettings
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> Set(SetCommand command, CancellationToken cancellationToken);

    public record SetCommand(Session Session, string ChatId, ChatUserSettings Settings) : ISessionCommand<Unit>;
}
