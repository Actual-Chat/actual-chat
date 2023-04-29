using ActualChat.App.Maui.Services;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ActualChat.App.Maui;

public static class MauiDiagnostics
{
    public static readonly ILoggerFactory LoggerFactory;
    public static readonly Tracer Tracer;

    static MauiDiagnostics()
    {
        Log.Logger = CreateSerilogLoggerConfiguration().CreateLogger();
        Tracer.Default = Tracer = CreateTracer();
        LoggerFactory = new SerilogLoggerFactory(Log.Logger);
        DefaultLog = LoggerFactory.CreateLogger("ActualChat.Unknown");
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = LoggerFactory.CreateLogger(typeof(WebMReader));
    }

    public static IServiceCollection AddMauiDiagnostics(this IServiceCollection services, bool dispose)
    {
        services.AddTracer(Tracer); // We don't want to have scoped tracers in MAUI app
        services.AddSingleton(LoggerFactory);
        services.Add(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        /*
        services.AddLogging(logging => {
            logging.ClearProviders();
            var minLevel = Log.Logger.IsEnabled(LogEventLevel.Debug)
                ? LogLevel.Debug
                : LogLevel.Information;
            logging
                .AddSerilog(Log.Logger, dispose: dispose)
                .SetMinimumLevel(minLevel);
        });
        */
        return services;
    }

    // Private methods

    private static LoggerConfiguration CreateSerilogLoggerConfiguration()
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("ActualChat.UI.Blazor.Services.AppReplicaCache", LogEventLevel.Debug)
            .Enrich.With(new ThreadIdEnricher())
            .Enrich.FromLogContext()
            .Enrich.WithProperty(Serilog.Core.Constants.SourceContextPropertyName, "app.maui");
        configuration = MauiProgram.ConfigurePlatformLogger(configuration);
        if (Constants.Sentry.EnabledFor.Contains(AppKind.MauiApp))
            configuration = configuration.WriteTo.Sentry(options => options.ConfigureForApp());
#if WINDOWS
        var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
        var timeSuffix = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var fileName = $"actual.chat.{timeSuffix}.log";
        configuration = configuration.WriteTo.File(
            Path.Combine(localFolder.Path, "Logs", fileName),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
            fileSizeLimitBytes: 20 * 1024 * 1024);
#endif
        return configuration;
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
