using ActualChat.Hosting;

namespace ActualChat.Users;

public class MobileSessions : IMobileSessions
{
    private ISessionResolver SessionResolver { get; }
    private ISessionFactory SessionFactory { get; }
    private IAuth Auth { get; }
    private ClientInfo ClientInfo { get; }
    private ICommander Commander { get; }

    public MobileSessions(IServiceProvider services)
    {
        SessionResolver = services.GetRequiredService<ISessionResolver>();
        SessionFactory = services.GetRequiredService<ISessionFactory>();
        Auth = services.GetRequiredService<IAuth>();
        ClientInfo = services.GetRequiredService<ClientInfo>();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<string> Get(CancellationToken cancellationToken)
    {
        if (SessionResolver.HasSession && !SessionResolver.Session.IsDefault())
            return SessionResolver.Session.Id.Value;

        var (tenantId, userAgent, ipAddress) = ClientInfo;
        var session = SessionFactory.CreateSession().WithTenantId(tenantId);
        var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress ?? "", userAgent ?? "");
        await Commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        return session.Id.Value;
    }
}
