namespace ActualChat.Rpc;

/// <summary>
/// Picks the host used for RPC connections, which may differ from the app's base
/// host — content URLs keep using the base host.
/// </summary>
public abstract class RpcEndpointSelector
{
    public static RpcEndpointSelector? Instance { get; set; }

    public static string ApplyTo(string baseUrl)
    {
        // Returns baseUrl with just its host replaced by the selected RPC endpoint.
        if (Instance is not { } instance)
            return baseUrl;

        var schemeEnd = baseUrl.IndexOf("://");
        if (schemeEnd < 0)
            return baseUrl;

        var hostStart = schemeEnd + 3;
        var hostEnd = baseUrl.IndexOfAny(['/', ':', '?'], hostStart);
        if (hostEnd < 0)
            hostEnd = baseUrl.Length;

        var host = baseUrl[hostStart..hostEnd];
        var endpoint = instance.Get(host);
        return string.Equals(host, endpoint, StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : string.Concat(baseUrl.AsSpan(0, hostStart), endpoint, baseUrl.AsSpan(hostEnd));
    }

    // Bumped whenever the selection is re-evaluated, including a reset that picks the
    // same endpoint again - a new network makes an old verdict meaningless.
    public abstract int Version { get; }

    public abstract string Get(string host);
    public abstract void UseDirect();
    public abstract bool MoveNext();
}
