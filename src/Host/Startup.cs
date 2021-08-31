using System.Collections.Immutable;
using System.Linq;
using ActualChat.Distribution.Module;
using ActualChat.Host.Internal;
using ActualChat.Host.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Collections;
using Stl.Plugins;
using Stl.Text;

namespace ActualChat.Host
{
    public class Startup
    {
        private IConfiguration Cfg { get; }
        private IWebHostEnvironment Env { get; }
        private IPluginHost Plugins { get; set; } = null!;
        private ImmutableArray<HostModule> HostModules { get; set; } = ImmutableArray<HostModule>.Empty;
        private ILogger Log => Plugins?.GetService<ILogger<Startup>>() ?? NullLogger<Startup>.Instance;

        public Startup(IConfiguration cfg, IWebHostEnvironment environment)
        {
            Cfg = cfg;
            Env = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Logging
            services.AddLogging(logging => {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
                if (Env.IsDevelopment()) {
                    logging.AddFilter(typeof(App).Namespace, LogLevel.Information);
                    logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Information);
                    // logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Transaction", LogLevel.Debug);
                    logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
                    logging.AddFilter("Stl.Fusion.Operations", LogLevel.Information);
                }
            });

            // HostInfo
            services.AddSingleton(new HostInfo() {
                HostKind = HostKind.WebServer,
                RequiredServiceScopes = ImmutableHashSet<Symbol>.Empty
                    .Add(ServiceScope.Server)
                    .Add(ServiceScope.BlazorUI),
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
            });

            // Creating plugins & host modules
            var pluginServices = new ServiceCollection()
                .Add(services)
                .AddSingleton(Cfg)
                .AddSingleton(Env);
            Plugins = new PluginHostBuilder(pluginServices).Build();
            HostModules = Plugins
                .GetPlugins<HostModule>()
                .OrderBy(m => m is not WebHostModule) // MainHostModule should be the first one
                .ToImmutableArray();

            // Using host modules to inject the remaining services
            HostModules.Apply(m => m.InjectServices(services));
        }

        public void Configure(IApplicationBuilder app, IHubRegistrar hubRegistrar)
            => HostModules.OfType<IWebHostModule>().Apply(m => m.ConfigureApp(app, hubRegistrar));
    }
}
