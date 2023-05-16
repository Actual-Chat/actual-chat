namespace ActualChat.UI.Blazor.App;

public sealed record AppLoadBalancerSettings(string RouteId)
{
    public static AppLoadBalancerSettings Default { get; } = new();

    // Google Cloud Load Balancer cookie header
    public (string Name, string Value) GclbCookieHeader { get; } = ("cookie", $"GCLB=\"{RouteId}\"");

    public AppLoadBalancerSettings()
        : this(Alphabet.Base16.Generator16.Next())
    { }
}
