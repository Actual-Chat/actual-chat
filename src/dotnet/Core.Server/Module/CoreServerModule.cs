using System.Diagnostics.CodeAnalysis;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualLab.Rpc;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreServerModule(IServiceProvider moduleServices) : HostModule<CoreServerSettings>(moduleServices)
{
    protected override CoreServerSettings ReadSettings()
        => Cfg.GetSettings<CoreServerSettings>(nameof(CoreSettings));

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        var hostKind = HostInfo.HostKind;
        if (hostKind.IsApp())
            throw StandardError.Internal("This module can be used on server side only.");

        // Core server-side services
        services.AddSingleton(c => new ServerSideServiceDefs(c));
        services.AddSingleton(c => new RpcCallShardMappers(c));
        services.AddSingleton<RpcBackendServiceDetector>(c => {
            var serverSideServiceDefs = c.GetRequiredService<ServerSideServiceDefs>();
            return (serviceType, serviceName) => serverSideServiceDefs.Contains(serviceType)
                || typeof(IBackendService).IsAssignableFrom(serviceType)
                || serviceType.Name.EndsWith("Backend", StringComparison.Ordinal)
                || serviceName.Value.StartsWith("backend.", StringComparison.Ordinal);
        });
        services.AddSingleton<RpcCallRouter>(c => {
            var serverSideServiceDefs = c.GetRequiredService<ServerSideServiceDefs>();
            var shardMappers = c.GetRequiredService<RpcCallShardMappers>();
            RpcHub? rpcHub = null;
            return (methodDef, arguments) => {
                rpcHub ??= c.RpcHub(); // We can't resolve it earlier, coz otherwise it will trigger recursion
                var serviceDef = methodDef.Service;
                if (!serviceDef.IsBackend)
                    throw StandardError.Internal("Only backend service methods can be called by servers.");

                var serverSideServiceDef = serverSideServiceDefs[serviceDef.Type];
                if (serverSideServiceDef.ServiceMode != ServiceMode.Client)
                    throw StandardError.Internal($"{serviceDef} must be a ServiceMode.Client service.");

                var sharding = Sharding.ByRole[serverSideServiceDef.ServerRole];
                var shardMapper = shardMappers[methodDef];
                var hash = shardMapper.Invoke(methodDef, arguments, sharding);
                var peerRef = sharding.GetClientPeerRef(hash);
                return rpcHub.GetClientPeer(peerRef);
            };
        });

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
