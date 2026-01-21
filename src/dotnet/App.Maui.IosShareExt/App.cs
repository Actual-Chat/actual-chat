using ActualChat.App.Maui.IosShareExt.Module;
using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.Hosting;
using ActualChat.Maui;
using ActualChat.Maui.Module;
using ActualChat.Module;
using ActualChat.Security;
using ActualChat.UI.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.Devices;

namespace ActualChat.App.Maui.IosShareExt;

#pragma warning disable VSTHRD002, IL2026
public static class App
{
    private static readonly ILogger BootstrapLog = new OSLogLogger(nameof(App));

    public static ServiceProvider Bootstrap()
    {
        try
        {
            MauiDiagnostics.Initialize();
            ClientStartup.Initialize();
            MauiDiagnostics.InitSentrySdk();
            MauiDiagnostics.CreateSentryTraceProvider();
            // TODO: StaticLog bootstrap
            var services = CreateServiceProvider();
            _ = services.GetRequiredService<SessionInitializer>().SetSession();

            return services;
        }
        catch (Exception e)
        {
            BootstrapLog.LogCritical(e, "App bootstrap failed.");
            throw;
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
            MauiSettings.BaseUrl);

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
            new UICoreModule(moduleServices),
            new MauiModule(moduleServices),
            new IosShareExtensionModule(moduleServices)
        );
        moduleHost.Build(services);
        return services.BuildServiceProvider();
    }
}
