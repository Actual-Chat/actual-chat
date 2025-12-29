using ActualChat.App.Maui.IosShareExt.Module;
using ActualChat.Hosting;
using ActualChat.Maui;
using ActualChat.Maui.Module;
using ActualChat.Module;
using ActualChat.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.Devices;

namespace ActualChat.App.Maui.IosShareExt;

#pragma warning disable VSTHRD002, IL2026
public static class App
{
    // TODO: use StaticLog instead
    private static readonly ILogger Log = new OSLogLogger(nameof(App));

    public static ServiceProvider Bootstrap()
    {
        try
        {
            ClientStartup.Initialize();
            // TODO: StaticLog bootstrap
            var services = CreateServiceProvider();
            _ = SetSession(services);
            return services;
        }
        catch (Exception e)
        {
            Log.LogCritical(e, "App bootstrap failed.");
            throw;
        }
    }

    private static async Task SetSession(ServiceProvider services)
    {
        try {
            // TODO: share constant for "Fusion.SessionID"
            var sessionId = await IosSharedSecureStorage.Default.GetAsync("Fusion.SessionId").ConfigureAwait(false);
            if (sessionId.IsNullOrEmpty()) {
                Log.LogCritical("No session id found.");
                return;
            }
            services.GetRequiredService<TrueSessionResolver>().Session = new Session(sessionId);
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to set session.");
        }
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var cfg = new ConfigurationManager();
        var env =
#if IS_PRODUCTION_ENV || !DEBUG
            Environments.Production;
#else
            Environments.Development;
#endif
        var baseUrl =
#if IS_DEV_MAUI
            $"https://{Constants.Hosts.DevVoxt}";
#else
        $"https://{Constants.Hosts.Voxt}"
#endif
        cfg.Sources.Add(new MemoryConfigurationSource() {
            InitialData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
                { "DOTNET_ENVIRONMENT", env },
            },
        });
        Constants.HostInfo = ClientStartup.CreateHostInfo(cfg,
            env,
            DeviceInfo.Current.Model,
            HostKind.MauiApp,
            AppKind.Ios,
            baseUrl);

        // ReSharper disable once VariableHidesOuterVariable
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Microsoft", LogLevel.Warning);

            logging.AddAppleUnifiedLog();
        });
        services.AddTracers(Tracer.Default, useScopedTracers: true);
        services.AddSingleton(_ => Constants.HostInfo);

        var moduleServices = services.BuildServiceProvider();
        var moduleHostBuilder = new ModuleHostBuilder();
        var moduleHost = moduleHostBuilder.AddModules(
            // From less dependent to more dependent!
            new CoreModule(moduleServices),
            new ApiModule(moduleServices),
            new ApiContractsModule(moduleServices),
            new MauiModule(moduleServices),
            new IosShareExtensionModule(moduleServices)
        );
        moduleHost.Build(services);
        return services.BuildServiceProvider();
    }
}
