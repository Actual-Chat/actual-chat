using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for user account management.
/// </summary>
public interface IAccountsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId?> GetIdByUserIdentity(UserIdentity identity, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserId?> GetIdByAlias(AliasId aliasId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<Account[]> ListChanged(
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

/// <summary>
/// Command to sign in a user with the given identity.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_SignIn(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] UserIdentity AuthenticatedIdentity,
    [property: DataMember, MemoryPackOrder(2)] ApiMap<UserIdentity, string> Identities, // May not include AuthenticatedIdentity
    [property: DataMember, MemoryPackOrder(3)] ApiMap<string, string> Claims,
    [property: DataMember, MemoryPackOrder(4)] bool MustExist = false
) : ISessionCommand<Unit>, IBackendCommand;

/// <summary>
/// Command to update a user account.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Update(
    [property: DataMember, MemoryPackOrder(0)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => Account.Id;
}

/// <summary>
/// Command to delete a user account.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record AccountsBackend_Delete(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}
