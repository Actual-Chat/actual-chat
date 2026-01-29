using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IAccountsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId?> GetIdByUserIdentity(UserIdentity identity, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId?> GetIdByAlias(AliasId aliasId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<UserId[]> ListChanged(
        long minVersion,
        long maxVersion,
        UserId? lastId,
        int limit,
        CancellationToken cancellationToken);

    Task<AccountFull?> GetLastChanged(CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnSignIn(AccountsBackend_SignIn command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnUpdate(AccountsBackend_Update command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDelete(AccountsBackend_Delete command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnNewAccountEvent(NewAccountEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_SignIn(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] UserIdentity AuthenticatedIdentity,
    [property: DataMember, MemoryPackOrder(2)] ApiMap<UserIdentity, string> Identities, // May not include AuthenticatedIdentity
    [property: DataMember, MemoryPackOrder(3)] ApiMap<string, string> Claims
) : ISessionCommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Update(
    [property: DataMember, MemoryPackOrder(0)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion
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
