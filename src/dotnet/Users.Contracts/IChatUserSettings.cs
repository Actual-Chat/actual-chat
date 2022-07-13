namespace ActualChat.Users;

public interface IChatUserSettings : IComputeService
{
    [ComputeMethod]
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
