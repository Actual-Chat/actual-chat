using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using ActualChat.Web.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Authentication;
using Stl.RestEase;

namespace ActualChat.Web.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class WebModule : HostModule, IWebModule
{
    public WebModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Fusion web server
        var fusion = services.AddFusion();
        fusion.AddWebServer();

        // Remove SessionMiddleware - just in case
        services.RemoveAll<SessionMiddleware.Options>();
        services.RemoveAll<SessionMiddleware>();
        services.AddSingleton(c => new SessionCookies(c));

        // RestEase client
        var restEase = services.AddRestEase();
        restEase.ConfigureHttpClient((c, name, o) => {
            o.HttpClientActions.Add(client => {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            });
        });

        // Controllers, etc.
        services.AddMvcCore(options => {
            options.ModelBinderProviders.Add(new ModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new ValidationMetadataProvider());
        });
    }

    public void ConfigureApp(IApplicationBuilder app)
    { }
}
