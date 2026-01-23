using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// Legacy IAuth interface for backwards compatibility with old Fusion clients.
/// Use IAccounts instead.
/// </summary>
[Obsolete("Use IAccounts instead.")]
public interface ILegacyAuth : IComputeService
{
    // Commands
    [CommandHandler]
    Task OnSignOut(LegacyAuth_SignOut command, CancellationToken cancellationToken = default);

    // Regular methods
    Task UpdatePresence(Session session, CancellationToken cancellationToken = default);

    // Queries
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionAuthInfo?> GetAuthInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default);
#pragma warning disable CS0618 // Type or member is obsolete
    [ComputeMethod(MinCacheDuration = 10)]
    Task<LegacyUser?> GetUser(Session session, CancellationToken cancellationToken = default);
#pragma warning restore CS0618
}

[Obsolete("Use Accounts_SignOut instead.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record LegacyAuth_SignOut(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool Force = false
) : ISessionCommand<Unit>;
