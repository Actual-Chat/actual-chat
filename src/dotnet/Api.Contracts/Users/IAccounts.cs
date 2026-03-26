namespace ActualChat.Users;

/// <summary>
/// Service for managing user accounts, sessions, and presence.
/// </summary>
public interface IAccounts : IComputeService
{
    // Commands
    [CommandHandler]
    Task OnSignOut(Accounts_SignOut command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdate(Accounts_Update command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeleteOwn(Accounts_DeleteOwn command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnCreateApiKey(Accounts_CreateApiKey command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeactivateSession(Accounts_DeactivateSession command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeactivateAllSessions(Accounts_DeactivateAllSessions command, CancellationToken cancellationToken);

    // Regular methods
    Task UpdatePresence(Session session, CancellationToken cancellationToken);

    // Queries
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<ApiList<UserSessionInfo>> GetOwnSessions(Session session, bool isApiKey, CancellationToken cancellationToken);

    // From IAuth
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_SignOut(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool Force = false
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_Update(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeleteOwn(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_CreateApiKey(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string Name,
    [property: DataMember, MemoryPackOrder(2)] Moment? ExpiresAt = null
) : ISessionCommand<string>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeactivateSession(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string IdPrefix
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeactivateAllSessions(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool ApiKeysOnly = false
) : ISessionCommand<Unit>, IApiCommand;
