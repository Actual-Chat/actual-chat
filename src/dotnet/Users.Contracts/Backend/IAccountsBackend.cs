using MemoryPack;

namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService
{
    [ComputeMethod]
    Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<UserId> GetIdByPhone(Phone phone, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<UserId> GetIdByEmail(string email, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnUpdate(AccountsBackend_Update command, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnDelete(AccountsBackend_Delete command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Update(
    [property: DataMember, MemoryPackOrder(0)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Delete(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand;
