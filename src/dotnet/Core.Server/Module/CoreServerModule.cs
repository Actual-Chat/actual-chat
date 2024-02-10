using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.AspNetCore;
using ActualChat.Rpc;
using ActualLab.Fusion.Server;
using ActualLab.Fusion.Server.Middlewares;
using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreServerModule(IServiceProvider moduleServices)
    : HostModule<CoreServerSettings>(moduleServices), IServerModule
{
    protected override CoreServerSettings ReadSettings()
        => Cfg.GetSettings<CoreServerSettings>(nameof(CoreSettings));

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        var hostKind = HostInfo.HostKind;
        if (!hostKind.IsServer())
            throw StandardError.Internal("This module can be used on server side only.");

        // Mesh services
        services.AddSingleton<MeshNode>(c => {
            var hostInfo = c.HostInfo();
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

                    host = Dns.GetHostName();
                }
            }

            var nodeId = new NodeRef(Generate.Option);
            var node = new MeshNode(
                nodeId, // $"{host}-{Ulid.NewUlid().ToString()}";
                $"{host}:{port.Format()}",
                hostInfo.Roles);
            Log.LogInformation("MeshNode: {MeshNode}", node.ToString());
            return node;
        });
        services.AddSingleton(c => new MeshWatcher(c));

        // Fusion server + Rpc configuration
        var fusion = services.AddFusion();
        var rpc = fusion.Rpc;
        fusion.AddWebServer();

        services.AddSingleton(c => new BackendServiceDefs(c));
        services.AddSingleton(c => new RpcMeshRefResolvers(c));
        services.AddSingleton(c => new RpcBackendDelegates(c));
        // Remove SessionMiddleware - we don't use it
        services.RemoveAll<SessionMiddleware.Options>();
        services.RemoveAll<SessionMiddleware>();
        // Replace RpcBackendServiceDetector
        services.AddSingleton<RpcBackendServiceDetector>(c => c.GetRequiredService<RpcBackendDelegates>().IsBackendService);
        // Replace RpcCallRouter
        services.AddSingleton<RpcCallRouter>(c => c.GetRequiredService<RpcBackendDelegates>().GetPeer);
        // Replace RpcServerConnectionFactory
        services.AddSingleton<RpcServerConnectionFactory>(c => c.GetRequiredService<RpcBackendDelegates>().GetConnection);
        // Replace DefaultSessionReplacerRpcMiddleware
        rpc.RemoveInboundMiddleware<DefaultSessionReplacerRpcMiddleware>();
        rpc.AddInboundMiddleware<RpcBackendDefaultSessionReplacerMiddleware>();
        // Replace RpcWebSocketClient
        services.AddSingleton<RpcWebSocketClient>(c => {
            var options = c.GetRequiredService<RpcWebSocketClient.Options>();
            return new RpcBackendWebSocketClient(options, c);
        });
        // Replace RpcClientPeerReconnectDelayer
        services.AddSingleton(c => new RpcClientPeerReconnectDelayer(c) { Delays = RetryDelaySeq.Exp(0.25, 10) });
        // Add RpcMethodActivityTracer
        services.AddSingleton<RpcMethodTracerFactory>(method => new RpcMethodActivityTracer(method) {
            UseCounters = true,
        });
        // Debug: add RpcRandomDelayMiddleware if you'd like to check how it works w/ delays
#if DEBUG && false
        rpc.AddInboundMiddleware(c => new RpcRandomDelayMiddleware(c) {
            Delay = new(0.2, 0.2), // 0 .. 0.4s
        });
#endif

        // Controllers, etc.
        services.AddRouting();
        var mvc = services.AddMvc(options => {
            options.ModelBinderProviders.Add(new BackendModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new BackendValidationMetadataProvider());
        });
        mvc.AddApplicationPart(GetType().Assembly);

        // Other services
        services.AddSingleton<IContentSaver>(c => new ContentSaver(c.BlobStorages()));
        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty()) {
            services.AddSingleton<IContentTypeProvider>(_ => ContentTypeProvider.Instance);
            services.AddSingleton(_ => new LocalFolderBlobStorage.Options());
            services.AddSingleton<IBlobStorages>(c => new LocalFolderBlobStorages(c));
        }
        else
            services.AddSingleton<IBlobStorages>(_ => new GoogleCloudBlobStorages(storageBucket));
    }
}
