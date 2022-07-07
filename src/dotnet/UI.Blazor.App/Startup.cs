using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using ActualChat.UI.Blazor.Module;
using ActualChat.Users.Client.Module;
using ActualChat.Users.UI.Blazor.Module;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App
{
    public static class Startup
    {
        public static async Task ConfigureServices(IServiceCollection services, Uri baseUri, params Type[] extraPluginTypes)
        {
            services.AddSingleton(_ => new UriMapper(baseUri));

            // Commander - it must be added first to make sure its options are set
            var commander = services.AddCommander(new CommanderOptions() { AllowDirectCommandHandlerCalls = false });

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
                typeof(UsersClientModule),
                typeof(UsersBlazorUIModule),
                typeof(FeedbackClientModule),
                typeof(NotificationClientModule),
                typeof(NotificationBlazorUIModule),
                typeof(BlazorUIAppModule)
            };
            if (extraPluginTypes != null)
                pluginTypes.AddRange(extraPluginTypes);
            pluginHostBuilder.UsePlugins(pluginTypes);
            var plugins = await pluginHostBuilder.BuildAsync().ConfigureAwait(false);
            services.AddSingleton(plugins);

            // Fusion services
            var fusion = services.AddFusion();
            var fusionClient = fusion.AddRestEaseClient();
            fusionClient.ConfigureHttpClient((c, name, o) => {
                var uriMapper = c.GetRequiredService<UriMapper>();
                var apiBaseUri = uriMapper.ToAbsolute("api/");
                var isFusionClient = (name ?? "").OrdinalStartsWith("Stl.Fusion");
                var clientBaseUri = isFusionClient ? baseUri : apiBaseUri;
                o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUri);
            });
            fusionClient.ConfigureWebSocketChannel(_ => new() {
                BaseUri = baseUri,
                LogLevel = LogLevel.Information,
                MessageLogLevel = LogLevel.None,
            });

            // Injecting plugin services
            plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));
        }
    }
}
