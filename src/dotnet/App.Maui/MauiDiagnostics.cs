using ActualChat.App.Maui.Services;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Configuration;
using Sentry;
using Sentry.Maui.Internal;
using Sentry.Serilog;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Exception = System.Exception;

namespace ActualChat.App.Maui;

public static class MauiDiagnostics
{
    private static readonly TimeSpan SentryStartDelay = TimeSpan.FromSeconds(5);
    private const string LogTag = "actual.chat";

    private static Exception? _configureLoggerException;
    private static SentryOptions? _sentryOptions;

    public static readonly ILoggerFactory LoggerFactory;
    public static readonly Tracer Tracer;

    static MauiDiagnostics()
    {
        Log.Logger = CreateSerilogLoggerConfiguration().CreateLogger();
        Tracer.Default = Tracer = CreateTracer();
        LoggerFactory = new SerilogLoggerFactory(Log.Logger);

        DefaultLog = LoggerFactory.CreateLogger("ActualChat.Unknown");
        if (_configureLoggerException != null)
            DefaultLog.LogError(_configureLoggerException, "Failed to configure logger");

        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = LoggerFactory.CreateLogger(typeof(WebMReader));

        if (_sentryOptions != null)
            _ = LoadingUI.WhenAppRendered.WithDelay(SentryStartDelay).ContinueWith(_ => {
                    InitSentrySdk(_sentryOptions);
                }, TaskScheduler.Default);
    }

    public static IServiceCollection AddMauiDiagnostics(this IServiceCollection services, bool dispose)
    {
        services.AddSingleton<Disposer>();
        services.AddTracer(Tracer); // We don't want to have scoped tracers in MAUI app
        services.AddLogging(logging => {
            logging.ClearProviders();
            var minLevel = Log.Logger.IsEnabled(LogEventLevel.Debug)
                ? LogLevel.Debug
                : LogLevel.Information;
            logging
                .AddSerilog(Log.Logger, dispose: dispose)
                .SetMinimumLevel(minLevel);
        });
        return services;
    }

    // Private methods

    private static LoggerConfiguration CreateSerilogLoggerConfiguration()
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.With(new ThreadIdEnricher())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui");
        configuration = ConfigureFromJsonFile(configuration, MauiProgram.GetAppSettingsFilePath());
        configuration = MauiProgram.ConfigurePlatformLogger(configuration);
        if (Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp))
            configuration = configuration.WriteTo.Sentry(ConfigureSentrySerilog);
        return configuration;
    }

    private static void ConfigureSentrySerilog(SentrySerilogOptions options)
    {
        options.ConfigureForApp();
        // We'll initialize the SDK later after app is loaded.
        options.InitializeSdk = false;

        // Set defaults for options that are different for MAUI.
        options.AutoSessionTracking = true;
        options.DetectStartupTime = StartupTimeDetectionMode.Fast;
        options.CacheDirectoryPath = FileSystem.CacheDirectory;

        // Global Mode makes sense for client apps
        options.IsGlobalModeEnabled = true;

        // We'll use an event processor to set things like SDK name
        options.AddEventProcessor(new SentryMauiEventProcessor2(options));

        _sentryOptions = options;
    }

    private static void InitSentrySdk(SentryOptions options)
    {
        // We can use MAUI's network connectivity information to inform the CachingTransport when we're offline.
        // We do it here to eliminate startup delay due to checking connectivity status in MauiNetworkStatusListener .ctor.
        options.NetworkStatusListener = new MauiNetworkStatusListener(Connectivity.Current, options);

        // Initialize the Sentry SDK.
        var disposable = SentrySdk.Init(options);
        // TODO(DF): It seems disposer is not invoked on application closing. Look up for another solution to flush client data.
        var disposer = AppServices.GetRequiredService<Disposer>();
        // Register the return value from initializing the SDK with the disposer.
        // This will ensure that it gets disposed when the service provider is disposed.
        disposer.Register(disposable);
    }

    private static LoggerConfiguration ConfigureFromJsonFile(
        LoggerConfiguration loggerConfiguration,
        string? filePath)
    {
        if (filePath == null) {
            LogInfo("No config file path provided");
            return loggerConfiguration;
        }

        try {
            LogInfo($"Config file path: '{filePath}'");
            var f = new FileInfo(filePath);
            if (!f.Exists) {
                LogInfo("Config file does not exist");
                return loggerConfiguration;
            }

            var loggerFilterOptions = ReadLoggerFilterRules(f);
            if (loggerFilterOptions.Rules.Count == 0) {
                LogInfo("No logger filter rules");
                return loggerConfiguration;
            }
            foreach (var rule in loggerFilterOptions.Rules) {
                if (!rule.LogLevel.HasValue)
                    continue;
                LogInfo($"Logger config. Category: '{rule.CategoryName}', Level: '{rule.LogLevel.Value}'");
                var logEventLevel = ToLogEventLevel(rule.LogLevel.Value);
                loggerConfiguration = rule.CategoryName != null
                    ? loggerConfiguration.MinimumLevel.Override(rule.CategoryName, logEventLevel)
                    : loggerConfiguration.MinimumLevel.Is(logEventLevel);
            }
        }
        catch (Exception e) {
            LogWarn(e, "Failed to configure logger");
            _configureLoggerException = e;
        }
        return loggerConfiguration;
    }

    private static LoggerFilterOptions ReadLoggerFilterRules(FileInfo f)
    {
        var loggerFilterOptions = new LoggerFilterOptions();
        var jsonConfig = new ConfigurationBuilder().AddJsonFile(f.FullName).Build();
        var loggerFilterConfigureOptions = new LoggerFilterConfigureOptions(jsonConfig);
        loggerFilterConfigureOptions.Configure(loggerFilterOptions);
        return loggerFilterOptions;
    }

    private static LogEventLevel ToLogEventLevel(LogLevel logLevel)
        => logLevel switch {
            LogLevel.Critical => LogEventLevel.Fatal,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Trace => LogEventLevel.Verbose,
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
        };

    private static void LogInfo(string message)
    {
#if ANDROID
        Android.Util.Log.Info(LogTag, message);
#endif
    }

    private static void LogWarn(Exception? exception, string message)
    {
#if ANDROID
        if (exception != null)
            Android.Util.Log.Warn(LogTag, Java.Lang.Throwable.FromException(exception),  message);
        else
            Android.Util.Log.Warn(LogTag, message);
#endif
    }

    private static Tracer CreateTracer()
    {
#if DEBUG
        var logger = Log.Logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "@trace");
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        return new Tracer("MauiApp", x => logger.Information(x.Format()));
#else
        return Tracer.None;
#endif
    }
}
