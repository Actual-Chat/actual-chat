namespace ActualChat.Users;

/// <summary>
/// Legacy IAuth implementation for backwards compatibility with old Fusion clients.
/// Use Accounts instead.
/// </summary>
[Obsolete("Use Accounts instead.")]
public class LegacyAuth(IServiceProvider services) : ILegacyAuth
{
    private ISessionsBackend SessionsBackend => field ??= services.GetRequiredService<ISessionsBackend>();
    private IAccountsBackend AccountsBackend => field ??= services.GetRequiredService<IAccountsBackend>();
    private ICommander Commander => field ??= services.Commander();

    // [CommandHandler]
    public virtual async Task OnSignOut(LegacyAuth_SignOut command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var backendCommand = new SessionsBackend_SignOut(command.Session, command.Force);
        await Commander.Call(backendCommand, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task UpdatePresence(Session session, CancellationToken cancellationToken = default)
        => SessionsBackend.UpdatePresence(session, cancellationToken);

    // Compute methods

    // [ComputeMethod]
    public virtual Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default)
        => SessionsBackend.IsSignOutForced(session, cancellationToken);

    // [ComputeMethod]
    public virtual Task<SessionAuthInfo?> GetAuthInfo(Session session, CancellationToken cancellationToken = default)
        => SessionsBackend.GetAuthInfo(session, cancellationToken);

    // [ComputeMethod]
    public virtual Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default)
        => SessionsBackend.Get(session, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<User?> GetUser(Session session, CancellationToken cancellationToken = default)
    {
        var authInfo = await SessionsBackend.GetAuthInfo(session, cancellationToken).ConfigureAwait(false);
        if (!(authInfo?.IsAuthenticated() ?? false))
            return null;

        return await AccountsBackend.GetUser(authInfo.UserId, cancellationToken).ConfigureAwait(false);
    }
}
