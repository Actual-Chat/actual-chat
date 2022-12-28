using System.Diagnostics.CodeAnalysis;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using Microsoft.Extensions.ObjectPool;
using Stl.Extensibility;
using Stl.Fusion.Client;
using Stl.Fusion.Extensions;
using Stl.Plugins;

namespace ActualChat.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class CoreModule : HostModule<CoreSettings>
{
    public CoreModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public CoreModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);

        var pluginAssemblies = Plugins.FoundPlugins.InfoByType
            .Select(c => c.Value.Type.TryResolve())
            .Where(c => c != null)
            .Select(c => c!.Assembly)
            .Distinct()
            .ToList();

        // Common services
        services.AddSingleton<UrlMapper>(sp => new UrlMapper(
            sp.GetRequiredService<HostInfo>()));

        // Matching type finder
        services.AddSingleton(new MatchingTypeFinder.Options {
            ScannedAssemblies = pluginAssemblies,
        });
        services.AddSingleton<IMatchingTypeFinder>(sp => new MatchingTypeFinder(
            sp.GetRequiredService<MatchingTypeFinder.Options>()));

        // DiffEngine
        services.AddSingleton<DiffEngine>(sp => new DiffEngine(sp));

        // ObjectPoolProvider & PooledValueTaskSourceFactory
        services.AddSingleton<ObjectPoolProvider>(_ => HostInfo.IsDevelopmentInstance
 #pragma warning disable CS0618
            ? new LeakTrackingObjectPoolProvider(new DefaultObjectPoolProvider())
 #pragma warning restore CS0618
            : new DefaultObjectPoolProvider());
        services.AddSingleton(typeof(IValueTaskSourceFactory<>), typeof(PooledValueTaskSourceFactory<>));

        // Fusion
        var fusion = services.AddFusion();
        fusion.AddComputedGraphPruner();
        fusion.AddFusionTime();

        // Features
        services.AddScoped<Features>(sp => new Features(sp));
        fusion.AddComputeService<IClientFeatures, ClientFeatures>(ServiceLifetime.Scoped);

        if (HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            InjectServerServices(services);
        if (HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            InjectClientServices(services);
    }

    private void InjectServerServices(IServiceCollection services)
    {
        services.AddSingleton<IContentSaver, ContentSaver>();

        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty())
            services.AddSingleton<IBlobStorageProvider>(new TempFolderBlobStorageProvider());
        else
            services.AddSingleton<IBlobStorageProvider>(new GoogleCloudBlobStorageProvider(storageBucket));

        var fusion = services.AddFusion();
        fusion.AddComputeService<IServerFeatures, ServerFeatures>();
    }

    private void InjectClientServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();
        var fusionClient = fusion.AddRestEaseClient();

        // Features
        fusionClient.AddReplicaService<ServerFeaturesClient.IClient, ServerFeaturesClient.IClientDef>();
        fusion.AddComputeService<IServerFeatures, ServerFeaturesClient>();
    }
}
