using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Module;

// ReSharper disable once NotAccessedPositionalProperty.Global
public sealed record AppLoadBalancerSettings(string RouteId)
{
    private static readonly Lock Lock = new();
    private static volatile AppLoadBalancerSettings _instance = new();

    public static AppLoadBalancerSettings Instance {
        get => _instance;
        set {
            lock (Lock)
                _instance = value;
        }
    }

    // Google Cloud Load Balancer cookie header
    public RpcHeader GclbCookieHeader { get; } = new("cookie", $"GCLB=\"{RouteId}\"");

    public AppLoadBalancerSettings()
        : this(Alphabet.Base16.Generator16.Next())
    { }
}
