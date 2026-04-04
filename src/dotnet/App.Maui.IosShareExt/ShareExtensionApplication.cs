using ActualChat.App.Maui.IosShareExt.Module;
using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.Hosting;
using ActualChat.Maui;
using ActualChat.Maui.Module;
using ActualChat.Module;
using ActualChat.UI.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace ActualChat.App.Maui.IosShareExt;

public class ShareExtensionApplication(ServiceProvider services) : IHasServices
{
    public IServiceProvider Services => services;

    public static ShareExtensionApplication? Bootstrap(UIViewController controller)
    {
        var log = new OSLogLogger(nameof(ShareViewController));

        try {
            Platform.Init(() => controller);
            MauiDiagnostics.Initialize();
            ClientStartup.Initialize();
            MauiDiagnostics.InitSentrySdk();
            MauiDiagnostics.CreateSentryTraceProvider();
            var services = CreateServiceProvider();
            _ = services.GetRequiredService<SessionInitializer>();
            return new ShareExtensionApplication(services);
        }
        catch (Exception e)
        {
            log.LogCritical(e, "Failed to bootstrap the app");
            return null;
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
