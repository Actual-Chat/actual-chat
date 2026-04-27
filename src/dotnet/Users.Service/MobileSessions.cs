using ActualChat.AspNetCore;
using Microsoft.AspNetCore.Http;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Users;

#pragma warning disable CS0618

public class MobileSessions(IServiceProvider services) : IMobileSessions
{
    private const string UnknownAppUserAgent = "UnknownApp/1.0";
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // Not a [ComputeMethod]!
    [Obsolete("2026.03: Use CreateSession(appVersion, ...) instead.")]
    public Task<Session> CreateSession(CancellationToken cancellationToken)
        => CreateSession("", cancellationToken);

    // Not a [ComputeMethod]!
    public async Task<Session> CreateSession(string appUserAgent, CancellationToken cancellationToken)
    {
        var session = Session.New();
        var (description, ipAddress) = GetDescriptionAndIPAddress(appUserAgent);
        var upsertSessionCmd = new SessionsBackend_Upsert(session) {
            IPAddress = ipAddress,
            Description = description,
        };
        await Commander.Call(upsertSessionCmd, true, cancellationToken).ConfigureAwait(false);
        return session;
    }

    // Not a [ComputeMethod]!
    [Obsolete("2026.03: Use ValidateSession(session, appVersion, ...) instead.")]
    public Task<Session> ValidateSession(Session session, CancellationToken cancellationToken)
        => ValidateSession(session, "", cancellationToken);

    // Not a [ComputeMethod]!
    public async Task<Session> ValidateSession(Session session, string appUserAgent, CancellationToken cancellationToken)
    {
        if (!await Accounts.IsValidSession(session, cancellationToken).ConfigureAwait(false))
            return await CreateSession(appUserAgent, cancellationToken).ConfigureAwait(false);

        var (description, ipAddress) = GetDescriptionAndIPAddress(appUserAgent);
        _ = SessionsBackend.UpdateLastSeenAt(session, description, ipAddress, CancellationToken.None);
        return session;
    }

    // Private methods

    private static (string Description, string IPAddress) GetDescriptionAndIPAddress(string appUserAgent)
    {
        var connection = RpcInboundContext.Current!.Peer.ConnectionState.Value.Connection!;
        var httpContext = connection.Properties.KeylessGet<HttpContext>()!;
        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";

        var description = appUserAgent.NullIfEmpty() ?? UnknownAppUserAgent;
        if (!userAgent.IsNullOrEmpty())
            description += $" {userAgent}";
        return (description, ipAddress);
    }
}
