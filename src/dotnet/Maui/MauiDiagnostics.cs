using ActualChat.Audio.WebM;
using ActualChat.Logging;
using ActualChat.Maui.Sentry;
using ActualChat.Maui.Sentry.Internal;
using ActualChat.UI;
using ActualLab.IO;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using OpenTelemetry.Trace;
using Sentry.Serilog;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;
using Tracer = ActualChat.Performance.Tracer;

namespace ActualChat.Maui;

#pragma warning disable CA1823 // Unused members - 'LogFolder', etc.

public static class MauiDiagnostics
{
    private const string AndroidOutputTemplate = "({ThreadID}) [{SourceContext}] {Message:l}{NewLine:l}{Exception}";

    private static SentryOptions _sentryOptions = null!;

    public static readonly string LogTag = MauiSettings.IsDevApp ? Constants.Hosts.DevVoxt : Constants.Hosts.Voxt;
    public static FilePath AppDataLogFilePath { get; private set; }
    public static TracerProvider? TracerProvider { get; private set; }
    public static bool IsAnalyticsCollectionEnabled { get; private set; }

    public static void Initialize()
    {
        MauiStartupBreadcrumbs.Initialize();
        Log.Logger = CreateAppLogger();
        StaticLog.Factory = new SanitizingLoggerFactory(
            new SerilogLoggerFactory(Log.Logger),
            mustSanitize: !MauiSettings.IsDevApp);
        Tracer.Default = CreateAppTracer();

        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = StaticLog.Factory.CreateLogger(typeof(WebMReader));
    }

    public static IServiceCollection AddMauiDiagnostics(this IServiceCollection services, bool dispose)
    {
        services.AddTracers(Tracer.Default, useScopedTracers: false);
        services.AddLogging(logging => {
            logging.ClearProviders();
            logging.ConfigureClientFilters(MauiSettings.AppKind);
            logging.AddFilteringSerilog(Log.Logger, dispose: dispose);
            logging.AddTailLogger();
            logging.AddSanitizingLoggerFactory(_ => !MauiSettings.IsDevApp);
        });
        return services;
    }

    public static void SetIsAnalyticsCollectionEnabled(bool isEnabled)
        => IsAnalyticsCollectionEnabled = isEnabled;

    // Private methods

    private static ILogger CreateAppLogger()
    {
        var logging = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With(new ThreadIdEnricher())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui");
            // .WriteTo.TailSink(); // TODO(Frol): uncomment when you fix the underlying issue
        logging = AddPlatformLoggerSinks(logging);
        // TODO(AY): ConfigureSentrySerilog takes ~30-50ms!
        // That's mostly it's SentryOptions static .ctor, so it makes sense
        // to inject a buffering sink that switches to pass-through when
        // the startup is completed.
        logging = logging.WriteTo.Sentry(ConfigureSentrySerilog);
        return logging.CreateLogger();
    }

    private static Tracer CreateAppTracer()
    {
        var logger = Log.Logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "@trace");
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        return new Tracer("MauiApp",
            isEnabled: () => true,
            writer: tracePoint => logger.Information(tracePoint.Format()));
    }

    private static LoggerConfiguration AddPlatformLoggerSinks(LoggerConfiguration logging)
    {
#if WINDOWS
        AppDataLogFilePath = Path.Combine(FileSystem.AppDataDirectory, "Logs", "ActualChat.log");
        logging = logging.WriteTo.Debug(outputTemplate: LoggingExt.OutputTemplate);
        // Without rollOnFileSizeLimit Serilog silently stops writing once the file hits
        // fileSizeLimitBytes - which left the Windows app with no logs at all for 2 years.
        logging = logging.WriteTo.File(AppDataLogFilePath,
            outputTemplate: LoggingExt.OutputTemplate,
            fileSizeLimitBytes: LoggingExt.FileSizeLimit,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: LoggingExt.RetainedFileCountLimit);
        if (!LoggingExt.DevLog.IsEmpty)
            logging = logging.WriteTo.File(LoggingExt.DevLog,
                outputTemplate: LoggingExt.OutputTemplate,
                fileSizeLimitBytes: LoggingExt.DevLogFileSizeLimit);
#elif ANDROID
        logging = logging
            .WriteTo.AndroidTaggedLog(LogTag, outputTemplate: AndroidOutputTemplate)
            .WriteTo.Sink(new AndroidFirebaseCrashlyticsSink());
#elif IOS
        logging = logging.WriteTo.AppleLog();
#endif
        return logging;
    }

    private static void ConfigureSentrySerilog(SentrySerilogOptions options)
    {
        options.ConfigureForApp(true);

        // We initialize the SDK later after the app is loaded.
        options.InitializeSdk = false;

        // Set defaults for options that are different for MAUI.
        options.AutoSessionTracking = true;
        options.DetectStartupTime = StartupTimeDetectionMode.Fast;
        options.CacheDirectoryPath = FileSystem.CacheDirectory;

        // Global Mode makes sense for client apps
        options.IsGlobalModeEnabled = true;

        options.MinimumEventLevel = LogEventLevel.Warning;

        // We'll use an event processor to set things like SDK name
        options.AddEventProcessor(new SentryMauiEventProcessor(options));
        _sentryOptions = options;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void InitSentrySdk()
        => InitSentrySdk(_sentryOptions);

    private static void InitSentrySdk(SentryOptions options)
    {
        // We can use MAUI's network connectivity information to inform the CachingTransport when we're offline.
        // We do it here to eliminate startup delay due to checking connectivity status in MauiNetworkStatusListener .ctor.
        options.NetworkStatusListener = new MauiNetworkStatusListener(Connectivity.Current, options);

        // Initialize the Sentry SDK.
        var disposable = SentrySdk.Init(options);
        // TODO(DF): It seems disposer is not invoked on application closing. Look up for another solution to flush client data.
        // var disposer = AppServices.GetRequiredService<Disposer>();
        // // Register the return value from initializing the SDK with the disposer.
        // // This will ensure that it gets disposed when the service provider is disposed.
        // disposer.Register(disposable);
    }

    public static void CaptureAndFlush(Exception error, TimeSpan flushTimeout)
    {
        // Both calls are no-ops when the SDK isn't initialized, so callers don't have to check
        SentrySdk.CaptureException(error);
        SentrySdk.Flush(flushTimeout);
    }

    public static void CreateSentryTraceProvider()
    {
        // TODO: fix PlatformNotSupportedException inside OpenTelemetry.
        if (OperatingSystem.IsIOS())
            return;

        TracerProvider = SentryExt.CreateSentryTraceProvider("MauiApp");
    }

    public static ILoggingBuilder AddFilteringSerilog(
        this ILoggingBuilder builder,
        ILogger? logger = null,
        bool dispose = false)
    {
        // NOTE(AY): It's almost the same code as in .AddSerilog, but with a single line commented out (see below)
        if (builder == null)
            throw new ArgumentNullException(nameof (builder));

        if (dispose)
            builder.Services.AddSingleton<ILoggerProvider, SerilogLoggerProvider>(
                _ => new SerilogLoggerProvider(logger, true));
        else
            builder.AddProvider(new SerilogLoggerProvider(logger));
        // builder.AddFilter<SerilogLoggerProvider>(null, LogLevel.Trace);
        return builder;
    }
}
