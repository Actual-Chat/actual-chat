using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;

namespace ActualChat;

public static partial class ServerEndpoints
{
    [GeneratedRegex(@"^(.*\/\/)?(.+):(\d+)")]
    private static partial Regex EndpointRegexFactory();

    public static readonly HashSet<Symbol> InternalHosts = [ "*", "localhost", "127.0.0.1", "0.0.0.0" ];

    public static string[] List(IServiceProvider services)
    {
        var server = services.GetRequiredService<IServer>();
        var endpoints = server.Features.Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? Array.Empty<string>();
        if (endpoints.Length != 0)
            return endpoints;

        var cfg = services.GetRequiredService<IConfiguration>();
        endpoints = (cfg.GetValue<string>("URLS") ?? "").Split(";");
        return endpoints;
    }

    public static (string Host, int Port) GetHttpEndpoint(IServiceProvider services)
    {
        var endpoints = List(services);
        var httpEndpoint = endpoints.FirstOrDefault(x => x.OrdinalStartsWith("http://"));
        if (httpEndpoint.IsNullOrEmpty())
            throw StandardError.Internal($"No server endpoint with http:// scheme: {endpoints.ToDelimitedString(";")}");

        var m = EndpointRegexFactory().Match(httpEndpoint);
        if (!m.Success)
            throw StandardError.Internal($"Can't parse server endpoint: {httpEndpoint}");

        var host = m.Groups[2].Value;
        var port = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return (host, port);
    }
}
