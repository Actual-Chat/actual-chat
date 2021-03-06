using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using Microsoft.Extensions.ObjectPool;
using Stl.Extensibility;
using Stl.Fusion.Extensions;
using Stl.Plugins;

namespace ActualChat.Module;

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

        // Matching type finder
        services.AddSingleton(new MatchingTypeFinder.Options() {
            ScannedAssemblies = pluginAssemblies,
        });
        services.AddSingleton<IMatchingTypeFinder, MatchingTypeFinder>();

        // DiffEngine
        services.AddSingleton<DiffEngine>();

        // ObjectPoolProvider & PooledValueTaskSourceFactory
        services.AddSingleton<ObjectPoolProvider>(_ => HostInfo.IsDevelopmentInstance
            ? new LeakTrackingObjectPoolProvider(new DefaultObjectPoolProvider())
            : new DefaultObjectPoolProvider());
        services.AddSingleton(typeof(IValueTaskSourceFactory<>), typeof(PooledValueTaskSourceFactory<>));

        // Fusion
        var fusion = services.AddFusion();
        fusion.AddFusionTime();
        if (HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            InjectServerServices(services);
    }

    private void InjectServerServices(IServiceCollection services)
    {
        services.AddSingleton<IContentSaver, ContentSaver>();

        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty())
            services.AddSingleton<IBlobStorageProvider, TempFolderBlobStorageProvider>();
        else
            services.AddSingleton<IBlobStorageProvider>(new GoogleCloudBlobStorageProvider(storageBucket));
    }
}
