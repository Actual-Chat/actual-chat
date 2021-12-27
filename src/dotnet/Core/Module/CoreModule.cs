using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
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
        // Common services
        services.AddSingleton<IMatchingTypeFinder>(_ => new MatchingTypeFinder());
        var fusion = services.AddFusion();
        fusion.AddFusionTime();

        if (HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            InjectServerServices(services);
    }

    private void InjectServerServices(IServiceCollection services)
    {
        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty())
            services.AddSingleton<IBlobStorageProvider, TempFolderBlobStorageProvider>();
        else
            services.AddSingleton<IBlobStorageProvider>(new GoogleCloudBlobStorageProvider(storageBucket));

        // TracingCommandHandler
        if (!services.HasService<TracingCommandHandler>()) {
            services.AddSingleton<TracingCommandHandler>();
            services.AddCommander().AddHandlers<TracingCommandHandler>();
        }
    }
}
