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
                o.HttpClientActions.Add(client => {
                    client.BaseAddress = clientBaseUrl.ToUri();
                    client.DefaultRequestVersion = HttpVersion.Version30;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                    if (appKind.IsMauiApp()) {
                        var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                        client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                    }
                });
                if (appKind.IsMauiApp())
                    o.HttpMessageHandlerBuilderActions.Add(b => {
                        if (b.PrimaryHandler is HttpClientHandler h)
                            h.UseCookies = false;
                    });
            });

            fusion.Rpc.AddWebSocketClient(c => {
                var trueSessionResolver = c.GetService<TrueSessionResolver>();
                var urlMapper = c.GetRequiredService<UrlMapper>();
                var baseUrl = urlMapper.BaseUrl.TrimSuffix("/");
                var isWebSocketUrl = baseUrl.StartsWith("ws://", StringComparison.Ordinal)
                    || baseUrl.StartsWith("wss://", StringComparison.Ordinal);
                if (!isWebSocketUrl) {
                    if (baseUrl.StartsWith("http://", StringComparison.Ordinal))
                        baseUrl = "ws://" + baseUrl.Substring(7);
                    else if (baseUrl.StartsWith("https://", StringComparison.Ordinal))
                        baseUrl = "wss://" + baseUrl.Substring(8);
                    else
                        baseUrl = "wss://" + baseUrl;
                }

                return new RpcWebSocketClient.Options() {
                    ConnectionUriResolver = (client, peer) => {
                        var settings = client.Settings;
                        using var sb = ZString.CreateStringBuilder();
                        var isDefaultClient = peer.Ref == RpcPeerRef.Default;
                        sb.Append(isDefaultClient ? baseUrl : $"ws://{peer.Ref.Id.Value}");
                        sb.Append(settings.RequestPath);
                        sb.Append('?');
                        sb.Append(settings.ClientIdParameterName);
                        sb.Append('=');
                        sb.Append(client.ClientId.UrlEncode());
                        if (isDefaultClient && trueSessionResolver is { HasSession: true }) {
                            sb.Append("&s=");
                            sb.Append(trueSessionResolver.Session.Id.UrlEncode());
                        }
                        return sb.ToString().ToUri();
                    },
                };
            });
            if (appKind.IsMauiApp())
                services.AddTransient<ClientWebSocket>(_ => {
                    var ws = new ClientWebSocket();
                    var gclbCookieHeader = AppLoadBalancerSettings.Instance.GclbCookieHeader;
                    ws.Options.SetRequestHeader(gclbCookieHeader.Name, gclbCookieHeader.Value);
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
