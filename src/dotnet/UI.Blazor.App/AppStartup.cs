using System.Net;
using System.Net.WebSockets;
using ActualChat.Audio.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Contacts.Module;
using ActualChat.Feedback.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Module;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.Notification.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using Cysharp.Text;
using Stl.Interception.Interceptors;
using Stl.RestEase;
using Stl.Rpc;
using Stl.Rpc.Clients;

namespace ActualChat.UI.Blazor.App
{
    public static class AppStartup
    {
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
                var clientBaseUrl = urlMapper.ApiBaseUrl;
                var trueSessionResolver = c.GetService<TrueSessionResolver>();
                o.HttpClientActions.Add(client => {
                    client.BaseAddress = clientBaseUrl.ToUri();
                    client.DefaultRequestVersion = HttpVersion.Version30;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                    if (appKind.IsMauiApp()) {
                        var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                        client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                        if (trueSessionResolver is { HasSession: true })
                            client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, trueSessionResolver.Session.Id.Value);
                    }
                });
                if (appKind.IsMauiApp())
                    o.HttpMessageHandlerBuilderActions.Add(b => {
                        if (b.PrimaryHandler is HttpClientHandler h)
                            h.UseCookies = false;
                    });
            });

            fusion.Rpc.AddWebSocketClient(c => {
                return new RpcWebSocketClient.Options() {
                    ConnectionUriResolver = (client, peer) => {
                        var settings = client.Settings;
                        var urlMapper = client.Services.GetRequiredService<UrlMapper>();
                        using var sb = ZString.CreateStringBuilder();
                        var isDefaultClient = peer.Ref == RpcPeerRef.Default;
                        if (isDefaultClient)
                            sb.Append(urlMapper.WebsocketBaseUrl);
                        else {
                            var addressAndPort = peer.Ref.Id.Value;
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
                };
            });
            services.AddTransient<ClientWebSocket>(c => {
                var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                if (!appKind.IsMauiApp())
                    return ws;

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
                    new InviteClientModule(moduleServices),
                    new NotificationClientModule(moduleServices),
                    // UI modules
                    new BlazorUICoreModule(moduleServices),
                    new AudioBlazorUIModule(moduleServices),
                    new UsersBlazorUIModule(moduleServices),
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
}
