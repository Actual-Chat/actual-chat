using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId> GetIdByUserIdentity(UserIdentity identity, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnUpdate(AccountsBackend_Update command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task OnDelete(AccountsBackend_Delete command, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ApiArray<UserId>> ListChanged(
        long minVersion,
        long maxVersion,
        UserId lastId,
        int limit,
        CancellationToken cancellationToken);

    Task<AccountFull?> GetLastChanged(CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Update(
    [property: DataMember, MemoryPackOrder(0)]
    AccountFull Account,
    [property: DataMember, MemoryPackOrder(1)]
    long? ExpectedVersion
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => Account.Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Delete(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
