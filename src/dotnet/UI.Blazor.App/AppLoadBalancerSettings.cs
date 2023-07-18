using Stl.Rpc.Infrastructure;

namespace ActualChat.UI.Blazor.App;

public sealed record AppLoadBalancerSettings(string RouteId)
{
    private static readonly object _lock = new();
    private static volatile AppLoadBalancerSettings _instance = new();

    public static AppLoadBalancerSettings Instance {
        get => _instance;
        set {
            lock (_lock)
                _instance = value;
        }
    }

    // Google Cloud Load Balancer cookie header
    public RpcHeader GclbCookieHeader { get; } = new("cookie", $"GCLB=\"{RouteId}\"");

    public AppLoadBalancerSettings()
        : this(Alphabet.Base16.Generator16.Next())
    { }
}
