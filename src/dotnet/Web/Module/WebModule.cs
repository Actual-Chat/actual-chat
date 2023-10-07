using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Builder;

namespace ActualChat.Web.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class WebModule : HostModule, IWebModule
{
    public WebModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        // Use AppServerModule instead
    }

    public void ConfigureApp(IApplicationBuilder app)
    {
        // Use AppServerModule instead
    }
}
