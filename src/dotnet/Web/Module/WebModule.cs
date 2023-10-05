using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Web.Internal;
using ActualChat.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Middlewares;
using Stl.Fusion.Server.Rpc;
using Stl.Rpc;
using Stl.Rpc.Diagnostics;
using Stl.Rpc.Testing;

namespace ActualChat.Web.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class WebModule : HostModule, IWebModule
{
    public WebModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Fusion web server
        var fusion = services.AddFusion();
        var rpc = fusion.Rpc;
        fusion.AddWebServer();

        // Add RpcMethodActivityTracer
        services.AddSingleton<RpcMethodTracerFactory>(method => new RpcMethodActivityTracer(method) {
            UseCounters = true,
        });

        // Add Rpc
#if DEBUG && false
        rpc.AddInboundMiddleware(c => new RpcRandomDelayMiddleware(c) {
            Delay = new(0.2, 0.2), // 0 .. 0.4s
        });
#endif

        // Remove SessionMiddleware - we use SessionCookies directly instead
        services.RemoveAll<SessionMiddleware.Options>();
        services.RemoveAll<SessionMiddleware>();

        // Replace RpcServerConnectionFactory with AppRpcConnectionFactory
        services.AddSingleton(_ => new AppRpcServerConnectionFactory());
        services.AddSingleton<RpcServerConnectionFactory>(c => c.GetRequiredService<AppRpcServerConnectionFactory>().Invoke);

        // Replace DefaultSessionReplacerRpcMiddleware with AppDefaultSessionReplacerRpcMiddleware
        rpc.RemoveInboundMiddleware<DefaultSessionReplacerRpcMiddleware>();
        rpc.AddInboundMiddleware<AppDefaultSessionReplacerRpcMiddleware>();

        // Controllers, etc.
        services.AddMvcCore(options => {
            options.ModelBinderProviders.Add(new ModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new ValidationMetadataProvider());
        });
    }

    public void ConfigureApp(IApplicationBuilder app)
    { }
}
