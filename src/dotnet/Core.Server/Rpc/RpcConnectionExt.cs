using ActualChat.AspNetCore;
using ActualChat.Resilience;
using ActualChat.Rpc.Internal;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Rpc;

public static class RpcConnectionExt
{
    public static string? GetRemoteIPAddress(this RpcConnection? connection)
    {
        // RpcBackendConnection snapshots the address at the WebSocket handshake, so its
        // request headers don't have to stay readable for the life of the connection.
        if (connection is RpcBackendConnection backendConnection)
            return backendConnection.RemoteIPAddress;

        return connection is not null && connection.Properties.KeylessTryGet<HttpContext>(out var httpContext)
            ? RateLimitIdentity.ToChargeableIP(httpContext.GetRemoteIPAddress())
            : null;
    }

    public static string? GetRemoteIPAddress(this RpcInboundContext? context)
        => context?.Peer.ConnectionState.Value.Connection.GetRemoteIPAddress();
}
