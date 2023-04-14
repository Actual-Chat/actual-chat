using System.Net;
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
using Stl.Interception.Interceptors; // Required for InterceptorBase configuration at Release

namespace ActualChat.UI.Blazor.App
{
    public static class AppStartup
    {
        private static readonly object _lock = new ();
        private static string? _sessionAffinityKey;

        public static string SessionAffinityKey {
            get {
                lock (_lock)
                    return _sessionAffinityKey ??= GenerateSessionAffinityKey();
            }
        }

        public static void ConfigureServices(
            IServiceCollection services,
            AppKind appKind,
            Func<IServiceProvider, HostModule[]>? platformModuleFactory = null)
        {
#if !DEBUG
            InterceptorBase.Options.Defaults.IsValidationEnabled = false;
#endif
            var tracer = Tracer.Default;

            // Commander - it must be added first to make sure its options are set
            var commander = services.AddCommander().Configure(new CommanderOptions() {
                AllowDirectCommandHandlerCalls = false,
            });

            // Fusion services
            var fusion = services.AddFusion();
            var fusionClient = fusion.AddRestEaseClient();
            const string cookieHeaderName = "cookie";
            var isWasm = appKind == AppKind.WasmApp;
            if (isWasm)
                fusionClient.ConfigureHttpClient((c, name, o) => {
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
                fusionClient.ConfigureHttpClient((c, name, o) => {
                    var urlMapper = c.GetRequiredService<UrlMapper>();
                    var isFusionClient = (name ?? "").OrdinalStartsWith("Stl.Fusion");
                    var clientBaseUrl = isFusionClient ? urlMapper.BaseUrl : urlMapper.ApiBaseUrl;
                    o.HttpClientActions.Add(client => {
                        client.BaseAddress = clientBaseUrl.ToUri();
                        client.DefaultRequestVersion = HttpVersion.Version30;
                        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                        client.DefaultRequestHeaders.Add(cookieHeaderName, GetCookieHeader());
                    });
                    o.HttpMessageHandlerBuilderActions.Add(b => {
                        if (b.PrimaryHandler is HttpClientHandler h)
                            h.UseCookies = false;
                    });
                });

            fusionClient.ConfigureWebSocketChannel(c => {
                var urlMapper = c.GetRequiredService<UrlMapper>();
                return new () {
                    BaseUri = urlMapper.BaseUri,
                    LogLevel = LogLevel.Information,
                    MessageLogLevel = LogLevel.None,
                    ClientWebSocketFactory = c1 => {
                        var client = WebSocketChannelProvider.Options.DefaultClientWebSocketFactory(c1);
                        if (!isWasm)
                            client.Options.SetRequestHeader(cookieHeaderName, GetCookieHeader());
                        return client;
                    },
                };
            });

            // Creating modules
            using var step = tracer.Region("Building and injecting module services");
            var moduleServices = new DefaultServiceProviderFactory().CreateServiceProvider(services);
            var moduleHostBuilder = new ModuleHostBuilder()
                // From less dependent to more dependent!
                .AddModules(
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
                moduleHostBuilder.AddModules(platformModuleFactory.Invoke(moduleServices));
            moduleHostBuilder.Build(services);
        }

        private static string GetCookieHeader()
            => $"GCLB=\"{SessionAffinityKey}\"";

        private static string GenerateSessionAffinityKey()
            => RandomStringGenerator.Default.Next(16, RandomStringGenerator.Base16Alphabet);
    }
}
