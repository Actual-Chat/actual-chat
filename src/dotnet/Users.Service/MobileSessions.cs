using Microsoft.AspNetCore.Http;
using Stl.Fusion.Server.Authentication;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Users;

public class MobileSessions : IMobileSessions
{
    public record Options
    {
        public static Options Default { get; set; } = new();

        public Func<HttpContext, Symbol> TenantIdExtractor { get; init; } = TenantIdExtractors.None;
    }

    private Options Settings { get; }
    private ISessionFactory SessionFactory { get; }
    private IAuth Auth { get; }
    private ICommander Commander { get; }

    public MobileSessions(Options settings, IServiceProvider services)
    {
        Settings = settings;
        SessionFactory = services.GetRequiredService<ISessionFactory>();
        Auth = services.GetRequiredService<IAuth>();
        Commander = services.Commander();
    }

    public virtual async Task<string> Create(CancellationToken cancellationToken)
    {
        // TODO(AK): refactor this to keep single place for tenantId extractor, etc.
        var httpContext = RpcInboundContext.Current!.Peer.ConnectionState.Value.Connection!.Options.Get<HttpContext>()!;
        var tenantId = Settings.TenantIdExtractor.Invoke(httpContext);
        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var session = SessionFactory.CreateSession().WithTenantId(tenantId);
        var setupSessionCommand = new AuthBackend_SetupSession(session, ipAddress ?? "", userAgent ?? "");
        await Commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        return session.Id.Value;
    }

    public virtual async Task<string> Validate(string sessionId, CancellationToken cancellationToken)
    {
        var existingSession = new Session(sessionId);
        var sessionInfo = await Auth.GetSessionInfo(existingSession, cancellationToken).ConfigureAwait(false);
        if (sessionInfo.IsStored())
            return sessionId;

        return await Create(cancellationToken).ConfigureAwait(false);
    }
}
