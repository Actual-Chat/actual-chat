using MemoryPack;

namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService
{
    [ComputeMethod]
    Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnUpdate(AccountsBackend_Update command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Update(
    [property: DataMember, MemoryPackOrder(0)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion
) : ICommand<Unit>, IBackendCommand;
