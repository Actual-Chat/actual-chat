namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService
{
    [ComputeMethod]
    Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] AccountFull Account,
        [property: DataMember] long? ExpectedVersion
        ) : ICommand<Unit>, IBackendCommand;
}
