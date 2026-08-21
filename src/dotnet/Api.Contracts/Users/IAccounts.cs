
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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_SignOut : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public bool Deactivate { get; init; } = false;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_Update : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required AccountFull Account { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeleteOwn : ApiCommand<Unit>;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_CreateApiKey : ApiCommand<string>
{
    [DataMember(Order = 2), Key(2)] public required string Name { get; init; }
    [DataMember(Order = 3), Key(3)] public int ExpiresInDays { get; init; } = 365;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeactivateSession : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string IdPrefix { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_DeactivateAllSessions : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required SessionKind[] Kinds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_ConfirmRegister : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Token { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Accounts_CancelRegister : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Token { get; init; }
}
