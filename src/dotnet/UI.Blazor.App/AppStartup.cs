using System.Diagnostics.CodeAnalysis;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Client;
using Stl.Generators;
using Stl.Interception.Interceptors;
using Stl.Plugins;

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

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CoreModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlaybackModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BlazorUICoreModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioBlazorUIModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ContactsClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatBlazorUIModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InviteClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UsersContractsModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UsersClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UsersBlazorUIModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FeedbackClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NotificationClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NotificationBlazorUIModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BlazorUIAppModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatModule))]
        public static async Task ConfigureServices(IServiceCollection services, AppKind appKind, params Type[] platformPluginTypes)
        {
#if !DEBUG
            InterceptorBase.Options.Defaults.IsValidationEnabled = false;
#endif
            var tracer = Tracer.Default;

            // Commander - it must be added first to make sure its options are set
            var commander = services.AddCommander().Configure(new CommanderOptions() {
                AllowDirectCommandHandlerCalls = false,
            });

            // Creating plugins
            var pluginHostBuilder = new PluginHostBuilder(new ServiceCollection().Add(services));
            // FileSystemPluginFinder doesn't work in Blazor, so we have to enumerate them explicitly
            var pluginTypes = new List<Type> {
                typeof(CoreModule),
                typeof(PlaybackModule),
                typeof(BlazorUICoreModule),
                typeof(AudioClientModule),
                typeof(AudioBlazorUIModule),
                typeof(ChatModule),
                typeof(ChatClientModule),
                typeof(ChatBlazorUIModule),
                typeof(ContactsClientModule),
                typeof(InviteClientModule),
                typeof(UsersContractsModule),
                typeof(UsersClientModule),
                typeof(UsersBlazorUIModule),
                typeof(UsersContractsModule),
                typeof(FeedbackClientModule),
                typeof(NotificationClientModule),
                typeof(NotificationBlazorUIModule),
                typeof(BlazorUIAppModule),
                typeof(ChatModule),
            };
            pluginTypes.AddRange(platformPluginTypes);
            pluginHostBuilder.UsePlugins(false, pluginTypes);

            var step = tracer.Region("Building PluginHost");
            var plugins = await pluginHostBuilder.BuildAsync().ConfigureAwait(false);
            step.Close();
            services.AddSingleton(plugins);

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

            // Injecting plugin services
            step = tracer.Region("Injecting plugin services");
            plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));
            step.Close();
        }

        private static string GetCookieHeader()
            => $"GCLB=\"{SessionAffinityKey}\"";

        private static string GenerateSessionAffinityKey()
            => RandomStringGenerator.Default.Next(16, RandomStringGenerator.Base16Alphabet);
    }
}
