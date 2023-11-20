using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.WebSockets;
using ActualChat.Audio.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Contacts.Module;
using ActualChat.Contacts.UI.Blazor.Module;
using ActualChat.Diff.Handlers;
using ActualChat.Feedback.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.Media.Module;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.Notification.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.Security;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using Cysharp.Text;
using Stl.CommandR.Interception;
using Stl.Fusion.Client.Interception;
using Stl.Fusion.Client.Internal;
using Stl.Fusion.Interception;
using Stl.Interception;
using Stl.Interception.Interceptors;
using Stl.Interception.Internal;
using Stl.RestEase;
using Stl.Rpc;
using Stl.Rpc.Clients;
using Stl.Rpc.Infrastructure;

namespace ActualChat.UI.Blazor.App;

#pragma warning disable IL2026 // Fine for module-like code

public static class AppStartup
{
    // Libraries
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PriorityQueue<,>))] // MemoryPack uses it
    // Stl.Interception
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InterfaceProxy))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypeViewInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypedFactoryInterceptor))]
    // Stl.Rpc
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcClientInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcRoutingInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundContextFactory))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Result<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ResultBox<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,,,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArgumentList<,,,,,,,,,>))]
    // CommandR
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CommandContext<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MethodCommandHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InterfaceCommandHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CommandServiceInterceptor))]
    // Fusion
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeMethodFunction<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcInboundComputeCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RpcOutboundComputeCall<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputeServiceInterceptor))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ClientComputeServiceInterceptor))]
    // Diffs
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MissingDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CloneDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullableDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RecordDiffHandler<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OptionDiffHandler<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SetDiffHandler<,>))]
    public static void ConfigureServices(
        IServiceCollection services,
        AppKind appKind,
        Func<IServiceProvider, HostModule[]>? platformModuleFactory = null)
    {
#if !DEBUG
            InterceptorBase.Options.Defaults.IsValidationEnabled = false;
#else
        if (appKind.IsMauiApp())
            InterceptorBase.Options.Defaults.IsValidationEnabled = false;
#endif
        var tracer = Tracer.Default;

        // Fusion services
        var fusion = services.AddFusion();
        var restEase = services.AddRestEase();
        restEase.ConfigureHttpClient((c, name, o) => {
            var urlMapper = c.GetRequiredService<UrlMapper>();
            var clientBaseUrl = urlMapper.ApiBaseUrl.ToUri();
            o.HttpClientActions.Add(client => {
                client.BaseAddress = clientBaseUrl;
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                // c.LogFor(typeof(AppStartup)).LogInformation(
                //     "HTTP client '{Name}' configured @ {BaseAddress}", name, client.BaseAddress);
                if (!appKind.IsMauiApp())
                    return;

                var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver) {
                    var session = trueSessionResolver.Session;
                    client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id.Value);
                }
            });
            if (appKind.IsMauiApp())
                o.HttpMessageHandlerBuilderActions.Add(b => {
                    if (b.PrimaryHandler is HttpClientHandler h)
                        h.UseCookies = false;
                });
        });

        fusion.Rpc.AddWebSocketClient(_ => new RpcWebSocketClient.Options() {
            ConnectionUriResolver = (client, peer) => {
                var settings = client.Settings;
                var urlMapper = client.Services.GetRequiredService<UrlMapper>();

                using var sb = ZString.CreateStringBuilder();
                if (peer.Ref == RpcPeerRef.Default)
                    sb.Append(urlMapper.WebsocketBaseUrl);
                else {
                    var addressAndPort = peer.Ref.Key.Value;
                    sb.Append(addressAndPort.OrdinalEndsWith(":443") ? "wss://" : "ws://");
                    sb.Append(addressAndPort);
                }
                sb.Append(settings.RequestPath);
                sb.Append('?');
                sb.Append(settings.ClientIdParameterName);
                sb.Append('=');
                sb.Append(client.ClientId.UrlEncode());
                return sb.ToString().ToUri();
            },
        });
        if (appKind.IsMauiApp())
            services.AddTransient<ClientWebSocket>(c => {
                // NOTE(AY): "new ClientWebSocket()" triggers this exception in WASM:
                // - PlatformNotSupportedException: Operation is not supported on this platform.
                // So the code below should never run in WASM.
                var ws = new ClientWebSocket();
                var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                ws.Options.SetRequestHeader(gclbCookieHeader.Name, gclbCookieHeader.Value);
                if (c.GetService<TrueSessionResolver>() is { HasSession: true } trueSessionResolver)
                    ws.Options.SetRequestHeader(Constants.Session.HeaderName, trueSessionResolver.Session.Id.Value);
                return ws;
            });

        // Creating modules
        using var _ = tracer.Region($"{nameof(ModuleHostBuilder)}.{nameof(ModuleHostBuilder.Build)}");
        var moduleServices = services.BuildServiceProvider();
        var moduleHostBuilder = new ModuleHostBuilder()
            // From less dependent to more dependent!
            .WithModules(
                // Core modules
                new CoreModule(moduleServices),
                // Generic modules
                new MediaPlaybackModule(moduleServices),
                // Service-specific & service client modules
                new AudioClientModule(moduleServices),
                new FeedbackClientModule(moduleServices),
                new UsersContractsModule(moduleServices),
                new UsersClientModule(moduleServices),
                new ContactsClientModule(moduleServices),
                new ChatModule(moduleServices),
                new ChatClientModule(moduleServices),
                new MediaClientModule(moduleServices),
                new InviteClientModule(moduleServices),
                new NotificationClientModule(moduleServices),
                // UI modules
                new BlazorUICoreModule(moduleServices),
                new AudioBlazorUIModule(moduleServices),
                new UsersBlazorUIModule(moduleServices),
                new ContactsBlazorUIModule(moduleServices),
                new ChatBlazorUIModule(moduleServices),
                new NotificationBlazorUIModule(moduleServices),
                // This module should be the last one
                new BlazorUIAppModule(moduleServices)
            );
        if (platformModuleFactory != null)
            moduleHostBuilder = moduleHostBuilder.WithModules(platformModuleFactory.Invoke(moduleServices));
        moduleHostBuilder.Build(services);
    }
}
