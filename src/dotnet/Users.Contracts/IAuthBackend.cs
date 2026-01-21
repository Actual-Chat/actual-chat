using ActualLab.Rpc;

namespace ActualChat.Users;

public interface IAuthBackend : IComputeService, IBackendService
{
    // Commands
    [CommandHandler]
    Task OnSignIn(AuthBackend_SignIn command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task<SessionInfo> OnSetupSession(AuthBackend_SetupSession command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSetOptions(AuthBackend_SetSessionOptions command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSignOut(Auth_SignOut command, CancellationToken cancellationToken = default);

    // Edit user name
    [CommandHandler]
    Task OnEditUser(Auth_EditUser command, CancellationToken cancellationToken = default);

    // Queries
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionAuthInfo?> GetAuthInfo(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableArray<SessionInfo>> GetUserSessions(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<User?> GetUser(string userId, CancellationToken cancellationToken = default);

    // For UpdatePresence - non-compute
    Task UpdatePresence(Session session, CancellationToken cancellationToken = default);
}
