using System;
using System.Threading.Tasks;
using ActualChat.Todos;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Icons.FontAwesome;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stl.Fusion;
using Stl.Fusion.Client;
using Stl.OS;
using Stl.DependencyInjection;
using Stl.Extensibility;
using Stl.Fusion.Blazor;
using Stl.Fusion.Extensions;

namespace ActualChat.UI.Blazor.Host
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            if (OSInfo.Kind != OSKind.WebAssembly)
                throw new ApplicationException("This app runs only in browser.");

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            ConfigureServices(builder.Services, builder);
            var host = builder.Build();
            host.Services.HostedServices().Start();
            return host.RunAsync();
        }

        public static void ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
        {
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter(typeof(App).Namespace, LogLevel.Information);

            var tmpServices = builder.Services.BuildServiceProvider();
            var log = tmpServices.GetRequiredService<ILogger<Program>>();

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

            // Using modules to register everything else
            var hostInfo = new HostInfo() {
                HostKind = HostKind.Blazor,
                ServiceScope = ServiceScope.Client,
                Environment = builder.HostEnvironment.Environment,
                Configuration = builder.Configuration,
            };
            services.UseModules()
                .ConfigureModuleServices(s => {
                    s.AddSingleton(log);
                    s.AddSingleton(hostInfo);
                })
                .Add<TodosClientModule>()
                .Use();

            ConfigureBlazorServerServices(services);
        }

        public static void ConfigureBlazorServerServices(IServiceCollection services)
        {
            // Blazorise
            services.AddBlazorise().AddBootstrapProviders().AddFontAwesomeIcons();

            // Other UI-related services
            var fusion = services.AddFusion();
            fusion.AddFusionTime();

            // Default update delay is 0.5s
            services.AddTransient<IUpdateDelayer>(_ => new UpdateDelayer(0.5));
        }
    }
}
