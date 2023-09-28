using ActualChat.UI.Blazor.Diagnostics;
using Sentry;
using Sentry.OpenTelemetry;

namespace ActualChat.UI.Blazor;

public static class SentryExt
{
    private const string UIDsn = "https://7bcdf3ac9a774dfab54df0e0a9865a20@o4504632882233344.ingest.sentry.io/4504639283789824";

    public static void ConfigureForApp(this SentryOptions options, bool useOpenTelemetry)
    {
        options.Dsn = UIDsn;
        options.AddExceptionFilterForType<OperationCanceledException>();
        options.Debug = false;
        options.DiagnosticLevel = SentryLevel.Error;

        options.CreateHttpMessageHandler = CreateHttpMessageHandler;

        if (useOpenTelemetry) {
            // TODO(DF): decide what sample settings we need to setup
            options.TracesSampleRate = 1;
            options.UseOpenTelemetry();
        }
    }

    private static HttpMessageHandler CreateHttpMessageHandler()
    {
        // We use ConditionalPropagatorHandler as custom transport
        // to disable telemetry context propagation during posting telemetry data to Sentry.
        // Otherwise it causes CORS request failure in WebAssembly mode.
        // Error:
        // Access to fetch at 'https://o4504632882233344.ingest.sentry.io/api/4504639283789824/envelope/'
        // from origin 'https://*.actual.chat'
        // has been blocked by CORS policy: Request header field traceparent is not allowed by Access-Control-Allow-Headers in preflight response.
        // See also:
        // https://github.com/dotnet/runtime/issues/85883
        // https://gist.github.com/MihaZupan/835591bb22270b1aa7feeeece721520d
        var handler = new HttpClientHandler();
        return new ConditionalPropagatorHandler(handler);
    }
}
