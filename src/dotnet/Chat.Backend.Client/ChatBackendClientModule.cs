﻿using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Chat.Backend.Client;

public class ChatBackendClientModule : HostModule
{
    public ChatBackendClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatBackendClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // NOOP for now, because we don't use several microservices and calls between them.
    }
}
