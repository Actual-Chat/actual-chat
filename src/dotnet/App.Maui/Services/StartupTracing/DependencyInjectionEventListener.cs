using System.Diagnostics.Tracing;
using Serilog;

namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DependencyInjectionEventListener : EventListener
{
    private readonly Serilog.ILogger _log;

    // Unfortunately it does not work in Mono and therefor in Android.
    // https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-collect-and-view-traces#eventlistener

    public DependencyInjectionEventListener()
        => _log = Log.Logger.ForContext<DependencyInjectionEventListener>();

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        _log.Information("OnEventSourceCreated: " + eventSource.Name);
        // https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection/src/DependencyInjectionEventSource.cs
        if(OrdinalEquals(eventSource.Name, "Microsoft-Extensions-DependencyInjection"))
            EnableEvents(eventSource, EventLevel.Verbose);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventId != 1)
            return;
        var message = eventData.EventName + " " + eventData.PayloadNames.Zip(eventData.Payload, (s, o) => $"{s}: '${o}'").ToCommaPhrase();
        _log.Information(message);
    }
}
