using Microsoft.AspNetCore.Http;
using Stl.Fusion.Server.Authentication;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Users;

#pragma warning disable CS0618

public class MobileSessions(IServiceProvider services) : IMobileSessions
{
    private IAuth Auth { get; } = services.GetRequiredService<IAuth>();
    private ICommander Commander { get; } = services.Commander();

    // Not a [ComputeMethod]!
    public async Task<Session> CreateSession(CancellationToken cancellationToken)
    {
        var httpContext = RpcInboundContext.Current!.Peer.ConnectionState.Value.Connection!.Options.Get<HttpContext>()!;
        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var session = Session.New();
        var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress, userAgent);
        await Commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        return session;
    }

    // Not a [ComputeMethod]!
    public async Task<Session> ValidateSession(Session session, CancellationToken cancellationToken)
    {
        if (!session.IsValid())
            return await CreateSession(cancellationToken).ConfigureAwait(false);

        var sessionInfo = await Auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo.IsStored()
            ? session
            : await CreateSession(cancellationToken).ConfigureAwait(false);
    }

    // Legacy API

    public async Task<string> Create(CancellationToken cancellationToken)
    {
        var session = await CreateSession(cancellationToken).ConfigureAwait(false);
        return session.Id.Value;
    }

    public async Task<string> Validate(string sessionId, CancellationToken cancellationToken)
    {
        var session = await ValidateSession(new Session(sessionId), cancellationToken).ConfigureAwait(false);
        return session.Id.Value;
    }
}
