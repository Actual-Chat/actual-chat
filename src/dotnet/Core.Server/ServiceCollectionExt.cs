using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.Rpc;

namespace ActualChat;

public static class ServiceCollectionExt
{
    public static BackendHostBuilder AddBackendHost(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
        => new(services, hostInfo, log);

    public static BackendServiceBuilder AddBackend(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
        => new(services, hostInfo, log);

    public static IServiceCollection AddMeshWatcher(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
    {
        // Mesh services
        services.AddSingleton<MeshNode>(c => {
            var host = Environment.GetEnvironmentVariable("POD_IP") ?? "";
            _ = int.TryParse(
                Environment.GetEnvironmentVariable("POD_PORT") ?? "80",
                CultureInfo.InvariantCulture,
                out var port);
            if (host.IsNullOrEmpty() || port == 0) {
                var endpoint = ServerEndpoints.List(c, "http://").FirstOrDefault();
                (host, port) = ServerEndpoints.Parse(endpoint);
                if (ServerEndpoints.InvalidHostNames.Contains(host)) {
                    if (!hostInfo.IsDevelopmentInstance)
                        throw StandardError.Internal($"Server host name is invalid: {host}");

                    host = "localhost";
                    // host = Dns.GetHostName();
                }
            }

            var nodeId = new NodeRef(Generate.Option);
            var node = new MeshNode(
                nodeId, // $"{host}-{Ulid.NewUlid().ToString()}";
                $"{host}:{port.Format()}",
                hostInfo.Roles);
            log?.LogInformation("MeshNode: {MeshNode}", node.ToString());
            return node;
        });
        services.AddSingleton(c => new MeshWatcher(c));
        return services;
    }
}
