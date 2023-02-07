using ActualChat.App.Server.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Console;
using Stl.Plugins;

namespace ActualChat.App.Server;

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
        services.AddScoped<CircuitTraceAccessor>();
        services.AddTraceSession(c => {
            var scopedAccessor = c.GetService<CircuitTraceAccessor>();
            return scopedAccessor ?? (ITraceAccessor)Program.RootTraceAccessor.Instance;
        });

        // Logging
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.AddConsole();
#pragma warning disable IL2026
            logging.AddConsoleFormatter<GoogleCloudConsoleFormatter, JsonConsoleFormatterOptions>();
#pragma warning restore IL2026
            logging.SetMinimumLevel(Env.IsDevelopment() ? LogLevel.Debug : LogLevel.Warning);
            // Use appsettings*.json to configure logging filters
        });

        // HostInfo
        services.AddSingleton(c => {
            var hostSettings = Cfg.GetSettings<HostSettings>();
            var baseUrl = hostSettings.BaseUrl;
            Func<string>? baseUrlProvider = null;
            if (baseUrl.IsNullOrEmpty()) {
                var server = c.GetRequiredService<IServer>();
                var serverAddressesFeature =
                    server.Features.Get<IServerAddressesFeature>()
                    ?? throw StandardError.NotFound<IServerAddressesFeature>("Can't get server address.");
                baseUrl = serverAddressesFeature.Addresses.FirstOrDefault();
                if (baseUrl.IsNullOrEmpty()) {
                    // If we can't figure out base url at the moment,
                    // lets define a base url provider to resolve base url later on demand.
                    string? resolvedBaseUrl = null;
                    baseUrlProvider = () => {
                        if (resolvedBaseUrl.IsNullOrEmpty()) {
                            resolvedBaseUrl = serverAddressesFeature.Addresses.FirstOrDefault()
                                ?? throw StandardError.NotFound<IServerAddressesFeature>(
                                    "No server addresses found. Most likely you trying to use UrlMapper before the server has started.");
                        }
                        return resolvedBaseUrl;
                    };
                }
            }

            return new HostInfo() {
                AppKind = hostSettings.AppKind ?? AppKind.WebServer,
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
                BaseUrl = baseUrl ?? "",
                BaseUrlProvider = baseUrlProvider,
            };
        });

        // Commander - it must be added first to make sure its options are set
        var commander = services.AddCommander().Configure(new CommanderOptions() {
            AllowDirectCommandHandlerCalls = false,
        });

        // Creating plugins & host modules
        var pluginServices = new ServiceCollection()
            .Add(services)
            .AddSingleton(Cfg)
            .AddSingleton(Env)
            .AddSingleton(new FileSystemPluginFinder.Options {
                AssemblyNamePattern = "ActualChat.*.dll",
            });

        // FileSystemPluginFinder cache fails on .NET 6 some times, so...
        /*
        var pluginHostBuilder = new PluginHostBuilder(pluginServices);
        pluginHostBuilder.UsePlugins(
            // Core modules
            typeof(CoreModule),
            typeof(PlaybackModule),
            typeof(DbModule),
            typeof(WebModule),
            typeof(BlazorUICoreModule),
            // Services
            typeof(AudioModule),
            typeof(AudioBlazorUIModule),
            typeof(TranscriptionModule),
            typeof(ChatModule),
            typeof(ChatBlazorUIModule),
            typeof(UsersModule),
            typeof(UsersBlazorUIModule),
            // "The rest of Startup.cs" module
            typeof(AppHostModule)
        );
        Plugins = pluginHostBuilder.Build();
        */
        Plugins = new PluginHostBuilder(pluginServices).Build();
        HostModules = Plugins
            .GetPlugins<HostModule>()
            .OrderBy(m => m is not AppHostModule) // MainHostModule should be the first one
            .ToImmutableArray();

        // Using host modules to inject the remaining services
        HostModules.Apply(m => m.InjectServices(services));
    }

    public void Configure(IApplicationBuilder app)
        => HostModules.OfType<IWebModule>().Apply(m => m.ConfigureApp(app));
}
