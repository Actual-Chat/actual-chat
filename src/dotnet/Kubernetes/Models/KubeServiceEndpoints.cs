using System.Text;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Kubernetes;

public record KubeServiceEndpoints(
    KubeService Service,
    ApiArray<KubeEndpoint> Endpoints = default,
    ApiArray<KubeEndpoint> ReadyEndpoints = default,
    ApiArray<KubePort> Ports = default)
{
    private readonly Dictionary<string, HashRing<string>> _hashRingCache = new(StringComparer.Ordinal);

    public KubePort? GetPort(string portName = "http")
    {
        foreach (var p in Ports)
            if (OrdinalEquals(p.Name, portName))
                return p;
        return null;
    }

    public HashRing<string> GetAddressHashRing(string portName = "http")
    {
        lock (_hashRingCache) {
            if (_hashRingCache.TryGetValue(portName, out var result))
                return result;
            result = BuildAddressHashRing();
            _hashRingCache[portName] = result;
            return result;
        }
    }

    protected virtual bool PrintMembers(StringBuilder builder)
    {
#pragma warning disable MA0011
        builder.Append("Service = ").Append(Service).Append(", ");
        builder.Append("Endpoints = [");
        foreach (var endpoint in Endpoints.Take(1))
            builder.Append(endpoint);
        foreach (var endpoint in Endpoints.Skip(1))
            builder.Append(", ").Append(endpoint);
        builder.Append("], ");
        builder.Append("Ports = [");
        foreach (var port in Ports.Take(1))
            builder.Append(port);
        foreach (var port in Ports.Skip(1))
            builder.Append(", ").Append(port);
        builder.Append(']');
#pragma warning restore MA0011
        return true;
    }

    // Private methods

    private HashRing<string> BuildAddressHashRing(string portName = "http")
    {
        var port = GetPort(portName);
        if (port == null)
            return HashRing<string>.Empty;

        // fallback to unready endpoints if none are ready
        var endpoints = ReadyEndpoints.Count > 0
            ? ReadyEndpoints
            : Endpoints;
        var addresses = endpoints
            .SelectMany(e => e.Addresses)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal);
        return new HashRing<string>(addresses, static a => a.GetDjb2HashCode());
    }
}
