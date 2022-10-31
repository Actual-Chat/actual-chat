namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService
{
    [ComputeMethod]
    Task<AccountFull?> Get(string id, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] AccountFull Account
        ) : ICommand<Unit>, IBackendCommand;
}
