using ActualChat.AspNetCore;
using ActualChat.Rpc;
using ActualChat.Rpc.Internal;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Resilience;

/// <summary>
/// What an inbound call is known to come from, whichever transport it arrived on.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct RateLimitSource(Session? Session, string? IPAddress)
{
    public static RateLimitSource ForHttp(HttpContext context)
        => new(TryGetSession(context), RateLimitIdentity.ToChargeableIP(context.GetRemoteIPAddress()));

    public static RateLimitSource ForConnection(RpcConnection? connection)
        => new((connection as RpcBackendConnection)?.Session, connection.GetRemoteIPAddress());

    // Private methods

    private static Session? TryGetSession(HttpContext context)
    {
        try {
            return context.TryGetSessionFromHeader()
                ?? context.TryGetSessionFromQuery()
                ?? context.TryGetSessionFromCookie();
        }
        catch (Exception) {
            return null;
        }
    }
}
