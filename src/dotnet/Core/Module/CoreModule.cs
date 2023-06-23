﻿using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Stl.Fusion.Extensions;
using Stl.Mathematics.Internal;
using Stl.RestEase;
using Stl.Rpc;

namespace ActualChat.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed partial class CoreModule : HostModule<CoreSettings>
{
    public CoreModule(IServiceProvider services) : base(services) { }

    protected internal override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);

        var appKind = HostInfo.AppKind;

        // Default binary serializer
        // ByteSerializer.Default = MessagePackByteSerializer.Default;

        // Common services
        services.AddTracer();
        services.AddSingleton(c => new StaticImportsInitializer(c));
        services.AddHostedService(c => c.GetRequiredService<StaticImportsInitializer>());
        services.AddSingleton(c => new UrlMapper(c.GetRequiredService<HostInfo>()));

        // IArithmetics
        services.AddTypeMapper<IArithmetics>(map => map
            .Add<double, DoubleArithmetics>()
            .Add<int, IntArithmetics>()
            .Add<long, LongArithmetics>()
            .Add<Moment, MomentArithmetics>()
            .Add<TimeSpan, TimeSpanArithmetics>()
        );

        // IDiffHandlers
        services.AddTypeMapper<IDiffHandler>(DiffEngine.DefaultTypeMapBuilder);

        // DiffEngine
        services.AddSingleton(c => new DiffEngine(c));

        // ObjectPoolProvider & PooledValueTaskSourceFactory
        services.AddSingleton<ObjectPoolProvider>(_ => HostInfo.IsDevelopmentInstance
 #pragma warning disable CS0618
            ? new LeakTrackingObjectPoolProvider(new DefaultObjectPoolProvider())
 #pragma warning restore CS0618
            : new DefaultObjectPoolProvider());

        // Fusion
        var fusion = services.AddFusion();
        if (appKind.IsServer()) {
            // It's quite important to make sure fusion.WithServiceMode call follows the very first
            // services.AddFusion call, otherwise every fusion.AddService(...) call that happens earlier
            // won't be affected by this mode change!
            fusion = fusion.WithServiceMode(RpcServiceMode.Server, true);
        }
        else if (appKind.IsClient()) {
            if (appKind.IsWasmApp() && HostInfo.IsDevelopmentInstance) {
                if (Constants.DebugMode.RpcClient)
                    services.AddSingleton<RpcPeerFactory>(_
                        => static (hub, peerRef) => peerRef.IsServer
                            ? throw StandardError.NotSupported("No server peers on the client.")
                            : new RpcClientPeer(hub, peerRef) { CallLogLevel = LogLevel.Debug });
            }
            else {
                RpcServiceRegistry.ConstructionDumpLogLevel = LogLevel.None;
            }
        }
        fusion.AddComputedGraphPruner();
        fusion.AddFusionTime();

        // Features
        services.AddScoped(c => new Features(c));
        fusion.AddService<IClientFeatures, ClientFeatures>(ServiceLifetime.Scoped);

        if (appKind.IsServer())
            InjectServerServices(services);
        if (appKind.IsClient())
            InjectClientServices(services);
    }

    private void InjectServerServices(IServiceCollection services)
    {
        services.AddSingleton<IContentSaver, ContentSaver>();

        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty()) {
            services.AddSingleton<IContentTypeProvider>(c
                => c.GetRequiredService<IOptions<StaticFileOptions>>().Value.ContentTypeProvider);
            services.AddSingleton<IBlobStorageProvider>(c => new TempFolderBlobStorageProvider(c));
        }
        else
            services.AddSingleton<IBlobStorageProvider>(new GoogleCloudBlobStorageProvider(storageBucket));

        var fusion = services.AddFusion();
        fusion.AddService<IServerFeatures, ServerFeatures>();
    }

    private void InjectClientServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();
        var restEase = services.AddRestEase();
        restEase.ConfigureHttpClient((c, name, o) => {
            o.HttpClientActions.Add(client => {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            });
        });

        // Features
        fusion.AddClient<IServerFeaturesClient>();
        fusion.Rpc.Service<IServerFeaturesClient>().HasName(nameof(IServerFeatures));
        fusion.AddService<IServerFeatures, ServerFeaturesClient>();
    }
}
