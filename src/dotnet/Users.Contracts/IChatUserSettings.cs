namespace ActualChat.Users;

public interface IChatUserSettings : IComputeService
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task Set(SetCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] ChatUserSettings Settings
        ) : ISessionCommand<Unit>;
}
