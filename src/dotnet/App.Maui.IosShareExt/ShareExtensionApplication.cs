using ActualChat.App.Maui.IosShareExt.Module;
using ActualChat.App.Maui.IosShareExt.Services;
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
    // Both block the main thread on the failure path, and an app extension that stops
    // responding for too long gets killed - so the worst case has to stay well under that.
    private static readonly TimeSpan SentryInitTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SentryFlushTimeout = TimeSpan.FromSeconds(2);

    public IServiceProvider Services => services;

    public static ShareExtensionApplication? Bootstrap(UIViewController controller)
    {
        var log = new OSLogLogger(nameof(ShareViewController));
        Task? whenSentryReady = null;

        try {
            Platform.Init(() => controller);
            MauiDiagnostics.Initialize();
            ClientStartup.Initialize();
            whenSentryReady = InitSentry(log);
            var services = CreateServiceProvider();
            _ = services.GetRequiredService<SessionInitializer>();
            return new ShareExtensionApplication(services);
        }
        catch (Exception e) {
            log.LogCritical(e, "Failed to bootstrap the app");
            ReportBootstrapFailure(e, whenSentryReady, log);
            return null;
        }
    }

    private static void ReportBootstrapFailure(Exception error, Task? whenSentryReady, ILogger log)
    {
        // Sentry is the only way a broken share sheet reaches us, so this path - unlike the
        // happy one - pays for it. A null task means the failure beat InitSentry to it, which
        // covers everything before the DI container exists: capturing without initializing
        // first would no-op against the disabled hub and silently drop the report.
        try {
            if (whenSentryReady is null)
                MauiDiagnostics.InitSentrySdk();
            else
                // WaitAny, not Wait: it reports completion, so a failed InitSentry doesn't throw
                Task.WaitAny([whenSentryReady], SentryInitTimeout);
            MauiDiagnostics.CaptureAndFlush(error, SentryFlushTimeout);
        }
        catch (Exception e) {
            log.LogError(e, "Failed to report the bootstrap failure");
        }
    }

    private static Task InitSentry(ILogger log)
        // Sentry's SDK init takes ~1s, a third of the time the share sheet needs to show
        // anything, so it runs off the startup path - the main app does the same via
        // MauiSentryInitializer, which starts it from a worker 10s after the app is rendered.
        => BackgroundTask.Run(() => {
            MauiDiagnostics.InitSentrySdk();
            MauiDiagnostics.CreateSentryTraceProvider();
            return Task.CompletedTask;
        }, log, "Failed to initialize Sentry");

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
            // Serilog writes to the Apple unified log too (MauiDiagnostics.AddPlatformLoggerSinks),
            // and going through it is what gets warnings and errors to Sentry - AddAppleUnifiedLog
            // reached os_log only, so nothing the extension logged was ever reported.
            logging.AddFilteringSerilog(Serilog.Log.Logger);
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
