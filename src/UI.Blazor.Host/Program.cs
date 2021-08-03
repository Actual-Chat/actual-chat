using System;
using System.Threading.Tasks;
using ActualChat.Hosting;
using ActualChat.Todos;
using ActualChat.Todos.Client;
using ActualChat.Todos.UI.Blazor;
using ActualChat.Voice.UI.Blazor;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Icons.FontAwesome;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Stl.Collections;
using Stl.Fusion;
using Stl.Fusion.Client;
using Stl.OS;
using Stl.DependencyInjection;
using Stl.Extensibility;
using Stl.Fusion.Blazor;
using Stl.Fusion.Extensions;
using Stl.Fusion.UI;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Host
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (OSInfo.Kind != OSKind.WebAssembly)
                throw new ApplicationException("This app runs only in browser.");

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            await ConfigureServices(builder.Services, builder);
            var host = builder.Build();
            await host.Services.HostedServices().Start();
            await host.RunAsync();
        }

        public static async Task ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
        {
            // Logging
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter(typeof(App).Namespace, LogLevel.Information);

            // Other services shared with plugins
            services.AddSingleton(new HostInfo() {
                HostKind = HostKind.Blazor,
                ServiceScope = ServiceScope.Client,
                Environment = builder.HostEnvironment.Environment,
                Configuration = builder.Configuration,
            });

            // Creating plugins
            var pluginHostBuilder = new PluginHostBuilder(new ServiceCollection().Add(services));
            // FileSystemPluginFinder doesn't work in Blazor, so we have to enumerate them explicitly
            pluginHostBuilder.UsePlugins(
                typeof(TodosClientModule),
                typeof(TodosBlazorUIModule),
                typeof(VoiceBlazorUIModule));
            var plugins = await pluginHostBuilder.BuildAsync();
            services.AddSingleton(plugins);

            var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
            var apiBaseUri = new Uri($"{baseUri}api/");

            // Fusion services
            var fusion = services.AddFusion();
            var fusionClient = fusion.AddRestEaseClient((_, o) => {
                o.BaseUri = baseUri;
                o.IsLoggingEnabled = true;
                o.IsMessageLoggingEnabled = false;
            });
            fusionClient.ConfigureHttpClientFactory((c, name, o) => {
                var isFusionClient = (name ?? "").StartsWith("Stl.Fusion");
                var clientBaseUri = isFusionClient ? baseUri : apiBaseUri;
                o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUri);
            });
            fusion.AddAuthentication().AddRestEaseClient().AddBlazor();

            // Injecting plugin services
            plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));

            // Blazor Server services
            ConfigureBlazorServerServices(services, plugins);
        }

        public static void ConfigureBlazorServerServices(IServiceCollection services, IPluginHost plugins)
        {
            // Blazorise
            services.AddBlazorise().AddBootstrapProviders().AddFontAwesomeIcons();

            // Other UI-related services
            var fusion = services.AddFusion();
            fusion.AddFusionTime();

            // Default update delay is 0.5s
            services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UICommandTracker(), 0.5));
        }
    }
}
