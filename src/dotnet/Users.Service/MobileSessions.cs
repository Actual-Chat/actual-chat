namespace ActualChat.Users;

public class MobileSessions : IMobileSessions
{
    private ISessionFactory SessionFactory { get; }
    private IAuth Auth { get; }
    // private ClientInfo ClientInfo { get; }
    private ICommander Commander { get; }

    public MobileSessions(IServiceProvider services)
    {
        SessionFactory = services.GetRequiredService<ISessionFactory>();
        Auth = services.GetRequiredService<IAuth>();
        // ClientInfo = services.GetRequiredService<ClientInfo>();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<string> Create(CancellationToken cancellationToken)
    {
        // var (tenantId, userAgent, ipAddress) = ClientInfo;
        var (tenantId, userAgent, ipAddress) = (Symbol.Empty, "", "");

        var session = SessionFactory.CreateSession().WithTenantId(tenantId);
        var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress ?? "", userAgent ?? "");
        await Commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        return session.Id.Value;
    }

    // [ComputeMethod]
    public virtual async Task<string> Validate(string sessionId, CancellationToken cancellationToken)
    {
        var existingSession = new Session(sessionId);
        var sessionInfo = await Auth.GetSessionInfo(existingSession, cancellationToken).ConfigureAwait(false);
        if (sessionInfo.IsStored())
            return sessionId;

        return await Create(cancellationToken).ConfigureAwait(false);
    }
}
