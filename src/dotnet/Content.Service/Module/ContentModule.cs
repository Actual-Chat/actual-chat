using ActualChat.Hosting;
using ActualChat.Web.Module;
using Content.Contracts;
using Microsoft.AspNetCore.Builder;
using Stl.Plugins;

namespace ActualChat.Content.Module;

public class ContentModule : HostModule<ContentSettings>, IWebModule
{
    public ContentModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ContentModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        services.AddResponseCaching();
        services.AddCommander().AddCommandService<IContentSaverBackend, ContentSaverBackend>();
    }

    public void ConfigureApp(IApplicationBuilder app)
    {
    }
}
