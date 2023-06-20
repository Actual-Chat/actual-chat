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
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using Stl.Fusion.Client;
using Stl.Generators;
// ReSharper disable once RedundantUsingDirective
using Stl.Interception.Interceptors;
using Stl.RestEase; // Required for InterceptorBase configuration at Release

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
            var isWasm = appKind == AppKind.WasmApp;
            if (isWasm)
                restEase.ConfigureHttpClient((c, name, o) => {
                    var urlMapper = c.GetRequiredService<UrlMapper>();
                    var isFusionClient = (name ?? "").OrdinalStartsWith("Stl.Fusion");
                    var clientBaseUrl = isFusionClient ? urlMapper.BaseUrl : urlMapper.ApiBaseUrl;
                    o.HttpClientActions.Add(client => {
                        client.BaseAddress = clientBaseUrl.ToUri();
                        client.DefaultRequestVersion = HttpVersion.Version30;
                        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                    });
                });
            else
                restEase.ConfigureHttpClient((c, name, o) => {
                    var urlMapper = c.GetRequiredService<UrlMapper>();
                    var isFusionClient = (name ?? "").OrdinalStartsWith("Stl.Fusion");
                    var clientBaseUrl = isFusionClient ? urlMapper.BaseUrl : urlMapper.ApiBaseUrl;
                    o.HttpClientActions.Add(client => {
                        var gclbCookieHeader = AppLoadBalancerSettings.Default.GclbCookieHeader;
                        client.BaseAddress = clientBaseUrl.ToUri();
                        client.DefaultRequestVersion = HttpVersion.Version30;
                        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                        client.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);
                    });
                    o.HttpMessageHandlerBuilderActions.Add(b => {
                        if (b.PrimaryHandler is HttpClientHandler h)
                            h.UseCookies = false;
                    });
                });

            fusion.Rpc.AddWebSocketClient(c => {
                var urlMapper = c.GetRequiredService<UrlMapper>();
                return urlMapper.BaseUri.ToString();
            });
            if (!isWasm) {
                services.AddTransient<ClientWebSocket>(_ => {
                    var ws = new ClientWebSocket();
                    var gclbCookieHeader = AppLoadBalancerSettings.Default.GclbCookieHeader;
                    ws.Options.SetRequestHeader(gclbCookieHeader.Name, gclbCookieHeader.Value);
                    return ws;
                });
            }

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
