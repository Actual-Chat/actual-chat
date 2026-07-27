using ActualChat.Rpc.Internal;
using ActualChat.Security;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Users;

public class SecureTokens(IServiceProvider services) : ISecureTokens
{
    private ISecureTokensBackend Backend { get; } = services.GetRequiredService<ISecureTokensBackend>();

    public async Task<SecureToken> CreateForSession(Session session, CancellationToken cancellationToken = default)
    {
        // On an inbound RPC call the session must come from the connection, never from the argument:
        // the argument is remote input, and whatever is signed here is later trusted as server-issued.
        // In-process callers (server-side rendering) have no inbound call, so they pass it explicitly.
        var inboundContext = RpcInboundContext.Current;
        if (inboundContext != null)
            session = inboundContext.Peer.ConnectionState.Value.Connection is RpcBackendConnection connection
                ? connection.Session
                : throw StandardError.Unauthorized("A session-bound RPC connection is required.");

        session.RequireValid();
        return await Backend.Create(SecureTokenKind.Session, session.Id, cancellationToken).ConfigureAwait(false);
    }
}
