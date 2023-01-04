namespace ActualChat.Users;

public interface IAccounts : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task InvalidateEverything(InvalidateEverythingCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] AccountFull Account,
        [property: DataMember] long? ExpectedVersion
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record InvalidateEverythingCommand(
        [property: DataMember] Session Session,
        [property: DataMember] bool Everywhere = false
        ) : ICommand<Unit>, IBackendCommand;
}
