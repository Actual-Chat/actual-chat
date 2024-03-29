using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace ActualChat;

public static partial class ServerEndpoints
{
    [GeneratedRegex(@"^(.*\/\/)?(.+):(\d+)")]
    private static partial Regex EndpointRegexFactory();
    private static Regex EndpointRegex { get; } = EndpointRegexFactory();

    public static readonly HashSet<Symbol> InvalidHostNames = [ "*", "localhost", "127.0.0.1", "0.0.0.0" ];

    public static string[] List(IServiceProvider services, string? prefix = null)
        => List(services.Configuration(), prefix);

    public static string[] List(IConfiguration configuration, string? prefix = null)
    {
        var endpoints = (configuration.GetValue<string>("URLS") ?? "").Split(";");
        if (!prefix.IsNullOrEmpty())
            endpoints = endpoints.Where(x => x.OrdinalStartsWith(prefix)).ToArray();
        return endpoints;
    }

    public static (string Host, int Port) Parse(string? endpoint)
        => TryParse(endpoint, out var host, out var port)
            ? (host, port)
            : throw new ArgumentOutOfRangeException(nameof(endpoint), $"Invalid endpoint: {endpoint}");

    public static bool TryParse(string? endpoint, out string host, out int port)
    {
        host = "";
        port = 0;
        if (endpoint.IsNullOrEmpty())
            return false;

        var m = EndpointRegex.Match(endpoint);
        if (!m.Success)
            return false;

        if (!int.TryParse(m.Groups[3].Value, CultureInfo.InvariantCulture, out port))
            return false;

        host = m.Groups[2].Value;
        return true;
    }
}
