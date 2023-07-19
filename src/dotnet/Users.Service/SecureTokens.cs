using ActualChat.Security;
using ActualChat.Web;
using Microsoft.AspNetCore.Http;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Users;

public class SecureTokens : ISecureTokens
{
    private ISecureTokensBackend Backend { get; }

    public SecureTokens(IServiceProvider services)
        => Backend = services.GetRequiredService<ISecureTokensBackend>();

    public async Task<SecureToken> Create(string value, CancellationToken cancellationToken = default)
        => await Backend.Create(value, cancellationToken).ConfigureAwait(false);

    public async Task<SecureToken> CreateForSession(Session session, CancellationToken cancellationToken = default)
    {
        if (session == Session.Default) {
            var rpcContext = RpcInboundContext.Current;
            if (rpcContext == null)
                throw StandardError.Unauthorized("Can not use Default session without Rpc context.");

            var httpContext = rpcContext.Peer.ConnectionState.Value.Connection!.Options.Get<HttpContext>()!;
            session = httpContext.GetSession();
        }
        return await Create(session.Id, cancellationToken).ConfigureAwait(false);
    }
}
