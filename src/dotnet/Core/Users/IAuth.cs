namespace ActualChat.Users;

public interface IAuth : IComputeService
{
    // Commands
    [CommandHandler]
    Task OnSignOut(Auth_SignOut command, CancellationToken cancellationToken = default);

    // Regular methods
    Task UpdatePresence(Session session, CancellationToken cancellationToken = default);

    // Queries
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionAuthInfo?> GetAuthInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<User?> GetUser(Session session, CancellationToken cancellationToken = default);
}
