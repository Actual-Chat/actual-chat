namespace ActualChat.Users;

public interface IAccounts : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Account?> Get(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Account?> GetByUserId(Session session, string userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Account Account
        ) : ISessionCommand<Unit>;
}
