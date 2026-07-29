using ActualLab.Rpc;

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
    [CommandHandler]
    Task OnConfirmRegister(Accounts_ConfirmRegister command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCancelRegister(Accounts_CancelRegister command, CancellationToken cancellationToken);

    // Queries
    // Inherits SessionsBackend.Get's error for an invalid session, and throws NotFound on its own
    // when the resolved user has no account - both are permanent for a given session.
    [ComputeMethod(MinCacheDuration = 60, ConsolidationDelay = 0.01, NonTransientErrorInvalidationDelay = 120)]
    Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60)]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfoFull?> GetSessionInfo(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<ApiList<SessionInfo>> ListOwnSessions(Session session, SessionKind kind, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_SignOut(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool Deactivate = false
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_Update(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? ExpectedVersion
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeleteOwn(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_CreateApiKey(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Name,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int ExpiresInDays = 365
) : ISessionCommand<string>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeactivateSession(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string IdPrefix
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeactivateAllSessions(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] SessionKind[] Kinds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_ConfirmRegister(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Token
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_CancelRegister(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Token
) : ISessionCommand<Unit>, IApiCommand;
