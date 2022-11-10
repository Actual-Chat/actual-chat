using System.Diagnostics.CodeAnalysis;
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
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App
{
    public static class AppStartup
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CoreModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PlaybackModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BlazorUICoreModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioClientModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioBlazorUIModule))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChatClientModule))]
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
