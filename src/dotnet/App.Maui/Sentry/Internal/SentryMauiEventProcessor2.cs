using Sentry.Extensibility;

namespace Sentry.Maui.Internal;

internal class SentryMauiEventProcessor2 : ISentryEventProcessor
{
    private readonly SentryOptions _options;

    public SentryMauiEventProcessor2(SentryOptions options)
        => _options = options;

    public SentryEvent Process(SentryEvent @event)
    {
        @event.Sdk.Name = Constants.SdkName;
        @event.Sdk.Version = Constants.SdkVersion;
        @event.Contexts.Device.ApplyMauiDeviceData(_options.DiagnosticLogger);
        @event.Contexts.OperatingSystem.ApplyMauiOSData(_options.DiagnosticLogger);

        return @event;
    }
}
