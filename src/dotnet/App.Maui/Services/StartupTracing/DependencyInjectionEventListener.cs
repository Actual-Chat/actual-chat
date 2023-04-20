using System.Diagnostics.Tracing;
using Cysharp.Text;

namespace ActualChat.App.Maui.Services.StartupTracing;

// Unfortunately it does not work in Mono and therefor in Android.
// https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-collect-and-view-traces#eventlistener
internal sealed class DependencyInjectionEventListener : EventListener
{
    private Serilog.ILogger? _log;

    private Serilog.ILogger Log
        => _log ??= Serilog.Log.Logger.ForContext<DependencyInjectionEventListener>();

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.Information($"{nameof(OnEventSourceCreated)}: {eventSource.Name}");
        // https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection/src/DependencyInjectionEventSource.cs
        if (OrdinalEquals(eventSource.Name, "Microsoft-Extensions-DependencyInjection"))
            EnableEvents(eventSource, EventLevel.Verbose);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventId != 1)
            return;

        var message = ZString.Concat(
            eventData.EventName, " ",
            eventData.PayloadNames!.Zip(eventData.Payload!, (s, o) => $"{s}: '${o}'").ToCommaPhrase());
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.Information(message);
    }
}
