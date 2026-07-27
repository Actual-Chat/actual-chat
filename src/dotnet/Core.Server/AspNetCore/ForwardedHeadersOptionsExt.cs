using System.Net;
using Microsoft.AspNetCore.Builder;
using IPNetwork = System.Net.IPNetwork;

namespace ActualChat.AspNetCore;

public static class ForwardedHeadersOptionsExt
{
    private static readonly char[] Separators = [';', ','];

    public static ForwardedHeadersOptions SetKnownProxies(
        this ForwardedHeadersOptions options,
        string networks,
        string proxies = "",
        ILogger? log = null)
    {
        // An unset ForwardLimit combined with a restricted peer list makes the walk stop at the first
        // X-Forwarded-For entry that isn't one of our own proxies - i.e. at the address the outermost
        // proxy actually saw, no matter how many entries the caller prepended.
        options.ForwardLimit = null;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var item in Split(networks))
            if (IPNetwork.TryParse(item, out var network))
                options.KnownIPNetworks.Add(network);
            else
                log?.LogWarning("ForwardedHeaders: can't parse network '{Network}'", item);
        foreach (var item in Split(proxies))
            if (IPAddress.TryParse(item, out var address))
                options.KnownProxies.Add(address);
            else
                log?.LogWarning("ForwardedHeaders: can't parse address '{Address}'", item);
        return options;
    }

    // Private methods

    private static string[] Split(string value)
        => value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
