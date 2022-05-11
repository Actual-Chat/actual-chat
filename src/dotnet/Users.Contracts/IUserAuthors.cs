namespace ActualChat.Users;

public interface IUserAuthors
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 10)]
    Task<string> GetName(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task UpdateName(UpdateNameCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateNameCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string Name
    ) : ISessionCommand<Unit>;
}
