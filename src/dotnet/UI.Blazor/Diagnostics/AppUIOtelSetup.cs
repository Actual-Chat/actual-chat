using ActualChat.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
// using Sentry.OpenTelemetry;

namespace ActualChat.UI.Blazor.Diagnostics;

public static class AppUIOtelSetup
{
    public static void SetupConditionalPropagator()
    {
        if (DistributedContextPropagator.Current is ConditionalPropagator)
            return;

        DistributedContextPropagator.Current = new ConditionalPropagator();
    }

    /// <summary>
    /// Create a trace provider that exports Actual.Chat telemetry to sentry
    /// </summary>
    /// <param name="serviceName"></param>
    /// <returns></returns>
    public static TracerProvider? CreateClientSentryTraceProvider(string serviceName)
        => Sdk.CreateTracerProviderBuilder()
            .AddSource(AppUIInstruments.ActivitySource.Name)
            .AddHttpClientInstrumentation(cfg => cfg.RecordException = true)
            .ConfigureResource(
                resource =>
                    resource.AddService(
                        serviceName: serviceName,
                        serviceVersion: AppUIInstruments.ActivitySource.Version))
            // .AddSentry() // <-- Configure OpenTelemetry to send traces to Sentry
            .Build();
}
