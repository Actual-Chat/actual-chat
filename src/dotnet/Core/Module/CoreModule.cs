﻿using ActualChat.Blobs;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
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
        var fusion = services.AddFusion();
        fusion.AddFusionTime();

        if (HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            InjectServerServices(services);
    }

    private void InjectServerServices(IServiceCollection services)
    {
        services.AddSingleton<IBlobStorageProvider, TempFolderBlobStorageProvider>();
    }
}
