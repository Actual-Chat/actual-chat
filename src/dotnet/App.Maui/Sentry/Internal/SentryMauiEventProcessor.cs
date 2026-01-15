using Sentry.Extensibility;

namespace Sentry.Maui.Internal;

internal class SentryMauiEventProcessor(SentryOptions options) : ISentryEventProcessor
{
    public SentryEvent Process(SentryEvent @event)
    {
        @event.Sdk.Name = Constants.SdkName;
        @event.Sdk.Version = Constants.SdkVersion;
        @event.Contexts.Device.ApplyMauiDeviceData(options.DiagnosticLogger);
        @event.Contexts.OperatingSystem.ApplyMauiOSData(options.DiagnosticLogger);

        return @event;
    }
}
