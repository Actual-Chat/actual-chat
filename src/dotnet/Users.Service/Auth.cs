namespace ActualChat.Users;

public class Auth(IServiceProvider services) : IAuth
{
    private IAuthBackend AuthBackend => field ??= services.GetRequiredService<IAuthBackend>();
    private ICommander Commander => field ??= services.Commander();

    // [CommandHandler]
    public virtual async Task OnSignOut(Auth_SignOut command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var backendCommand = new AuthBackend_SignOut(
            command.Session,
            command.KickUserSessionHash,
            command.KickAllUserSessions,
            command.Force);
        await Commander.Call(backendCommand, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task UpdatePresence(Session session, CancellationToken cancellationToken = default)
        => AuthBackend.UpdatePresence(session, cancellationToken);

    // Compute methods

    // [ComputeMethod]
    public virtual Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default)
        => AuthBackend.IsSignOutForced(session, cancellationToken);

    // [ComputeMethod]
    public virtual Task<SessionAuthInfo?> GetAuthInfo(Session session, CancellationToken cancellationToken = default)
        => AuthBackend.GetAuthInfo(session, cancellationToken);

    // [ComputeMethod]
    public virtual Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default)
        => AuthBackend.GetSessionInfo(session, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<User?> GetUser(Session session, CancellationToken cancellationToken = default)
    {
        var authInfo = await AuthBackend.GetAuthInfo(session, cancellationToken).ConfigureAwait(false);
        if (!(authInfo?.IsAuthenticated() ?? false))
            return null;

        return await AuthBackend.GetUser(authInfo.UserId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual Task<ImmutableArray<SessionInfo>> GetUserSessions(Session session, CancellationToken cancellationToken = default)
        => AuthBackend.GetUserSessions(session, cancellationToken);
}
