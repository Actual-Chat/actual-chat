namespace ActualChat.Users;

public interface IUserProfiles : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<UserProfile?> Get(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<UserProfile?> GetByUserId(Session session, string userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] UserProfile UserProfile
        ) : ISessionCommand<Unit>;
}
