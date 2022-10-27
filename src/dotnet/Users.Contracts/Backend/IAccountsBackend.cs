namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService
{
    [ComputeMethod]
    Task<Account?> Get(string id, CancellationToken cancellationToken);

    [CommandHandler]
    public Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Account Account
        ) : ICommand<Unit>, IBackendCommand;
}
