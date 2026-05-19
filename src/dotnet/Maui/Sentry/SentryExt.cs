using ActualChat.Diagnostics;
using ActualChat.UI.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sentry.OpenTelemetry;

namespace ActualChat.Maui.Sentry;

public static class SentryExt
{
    private const string UIDsn = "https://7bcdf3ac9a774dfab54df0e0a9865a20@o4504632882233344.ingest.sentry.io/4504639283789824";
    private const int ThrottleMaxPerWindow = 5;
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, ThrottleEntry> ThrottleTracker = new();
    private static long _lastThrottleSweepAtTicks = DateTime.UtcNow.Ticks;

    public static void ConfigureForApp(this SentryOptions options, bool useOpenTelemetry)
    {
        options.Dsn = UIDsn;
        options.SetBeforeSend(static (e, _) => {
            if (IsBelowReportableVersion())
                return null;
            if (IsThrottled(e))
                return null;

            return e;
        });
        options.AddExceptionFilterForType<OperationCanceledException>();
        options.Debug = false;
        options.DiagnosticLevel = SentryLevel.Error;
        if (MauiSettings.IsDevApp)
            options.Environment = "dev";
        options.CreateHttpMessageHandler = CreateHttpMessageHandler;
        options.AttachStacktrace = true;
        options.SendDefaultPii = false;

        if (useOpenTelemetry) {
            // TODO(DF): decide what sample settings we need to setup
            options.TracesSampleRate = 1;
            options.UseOpenTelemetry();
        }
    }

    private static bool IsBelowReportableVersion()
    {
        var minStr = MauiPreferences.MinReportableClientVersion;
        if (minStr.IsNullOrEmpty())
            return false;

        return Version.TryParse(minStr, out var min)
            && ApiConstants.Version < min;
    }

    private static bool IsThrottled(SentryEvent e)
    {
        var key = TryGetThrottleKey(e);
        if (key.IsNullOrEmpty())
            return false;

        var now = DateTime.UtcNow;
        SweepStaleThrottleEntries(now);
        var entry = ThrottleTracker.GetOrAdd(key, static _ => new ThrottleEntry());
        lock (entry) {
            if (now - entry.WindowStart > ThrottleWindow) {
                entry.WindowStart = now;
                entry.Count = 0;
            }
            entry.Count++;
            return entry.Count > ThrottleMaxPerWindow;
        }
    }

    private static void SweepStaleThrottleEntries(DateTime now)
    {
        var lastTicks = Interlocked.Read(ref _lastThrottleSweepAtTicks);
        if (now.Ticks - lastTicks <= ThrottleWindow.Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastThrottleSweepAtTicks, now.Ticks, lastTicks) != lastTicks)
            return;

        foreach (var pair in ThrottleTracker)
            if (now - pair.Value.WindowStart > ThrottleWindow)
                ThrottleTracker.TryRemove(pair);
    }

    private static string TryGetThrottleKey(SentryEvent e)
    {
        var ex = e.SentryExceptions?.FirstOrDefault();
        if (ex == null)
            return e.Message?.Formatted ?? "";

        var topFrame = ex.Stacktrace?.Frames.LastOrDefault();
        return $"{ex.Type}|{ex.Value}|{topFrame?.Module}.{topFrame?.Function}";
    }

    /// <summary>
    /// Create a trace provider that exports voxt.ai telemetry to sentry
    /// </summary>
    /// <param name="serviceName"></param>
    /// <returns></returns>
    public static TracerProvider CreateSentryTraceProvider(string serviceName)
        => Sdk.CreateTracerProviderBuilder()
            .AddSource(AppUIInstruments.ActivitySource.Name)
            .AddHttpClientInstrumentation(cfg => cfg.RecordException = true)
            .ConfigureResource(
                resource =>
                    resource.AddService(
                        serviceName: serviceName,
                        serviceVersion: AppUIInstruments.ActivitySource.Version))
            .AddSentry() // <-- Configure OpenTelemetry to send traces to Sentry
            .Build();

    private static HttpMessageHandler CreateHttpMessageHandler()
    {
        // We use ConditionalPropagatorHandler as custom transport
        // to disable telemetry context propagation during posting telemetry data to Sentry.
        // Otherwise, it causes CORS request failure in WebAssembly mode.
        // Error:
        // Access to fetch at 'https://o4504632882233344.ingest.sentry.io/api/4504639283789824/envelope/'
        // from origin 'https://*.voxt.ai'
        // has been blocked by CORS policy: Request header field traceparent is not allowed by Access-Control-Allow-Headers in preflight response.
        // See also:
        // https://github.com/dotnet/runtime/issues/85883
        // https://gist.github.com/MihaZupan/835591bb22270b1aa7feeeece721520d
        var handler = new HttpClientHandler();
        return new ConditionalPropagatorHandler(handler);
    }

    // Nested types

    private sealed class ThrottleEntry
    {
        public int Count;
        public DateTime WindowStart = DateTime.UtcNow;
    }
}
