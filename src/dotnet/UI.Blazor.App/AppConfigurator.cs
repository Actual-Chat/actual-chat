using ActualChat.Audio.Client.Module;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Chat.Client.Module;
using ActualChat.Chat.Module;
using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Feedback.Client.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Client.Module;
using ActualChat.MediaPlayback.Module;
using ActualChat.Module;
using ActualChat.Notification.Client.Module;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Client.Module;
using ActualChat.Users.Module;
using ActualChat.Users.UI.Blazor.Module;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App
{
    public static class AppConfigurator
    {
        public static async Task ConfigureServices(IServiceCollection services, params Type[] platformPluginTypes)
        {
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
                typeof(InviteClientModule),
                typeof(UsersContractsModule),
                typeof(UsersClientModule),
                typeof(UsersBlazorUIModule),
                typeof(FeedbackClientModule),
                typeof(NotificationClientModule),
                typeof(NotificationBlazorUIModule),
                typeof(BlazorUIAppModule),
            };
            pluginTypes.AddRange(platformPluginTypes);
            pluginHostBuilder.UsePlugins(pluginTypes);
            var plugins = await pluginHostBuilder.BuildAsync().ConfigureAwait(false);
            services.AddSingleton(plugins);

            // Fusion services
            var fusion = services.AddFusion();
            var fusionClient = fusion.AddRestEaseClient();
            fusionClient.ConfigureHttpClient((c, name, o) => {
                var urlMapper = c.GetRequiredService<UrlMapper>();
                var isFusionClient = (name ?? "").OrdinalStartsWith("Stl.Fusion");
                var clientBaseUrl = isFusionClient ? urlMapper.BaseUrl : urlMapper.ApiBaseUrl;
                o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUrl.ToUri());
            });
            fusionClient.ConfigureWebSocketChannel(c => {
                var urlMapper = c.GetRequiredService<UrlMapper>();
                return new () {
                    BaseUri = urlMapper.BaseUri,
                    LogLevel = LogLevel.Information,
                    MessageLogLevel = LogLevel.None,
                };
            });

            // Injecting plugin services
            plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));
        }
    }
}
