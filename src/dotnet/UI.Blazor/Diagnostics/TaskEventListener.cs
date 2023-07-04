using System.Diagnostics.Tracing;
using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Diagnostics;

// Requires <TrimmerRootAssembly Include="System.Private.CoreLib" />
public class TaskEventListener : WorkerBase
{
    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public Sampler Sampler { get; init; } = Sampler.EveryNth(128);

    public TaskEventListener(IServiceProvider services) : base()
    {
        Services = services;
        Log = services.LogFor(GetType());
    }
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var tEventSource = Type.GetType("System.Threading.Tasks.TplEventSource")!;
        var fLog = tEventSource.GetField("Log", BindingFlags.Static | BindingFlags.Public)!;
        var eventSource = (fLog.GetValue(null) as EventSource)!;

        var listener = new MyEventListener();
        listener.EventWritten += OnEvent;
        listener.EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)8);

        Log.LogInformation("Started, IsEnabled: {IsEnabled}", eventSource.IsEnabled(EventLevel.Informational, (EventKeywords)8));

        while (true) // Max delay for Task.Delay is ~ 49 days
            await Task.Delay(TimeSpan.FromDays(30), cancellationToken).ConfigureAwait(false);
    }

    private void OnEvent(object? source, EventWrittenEventArgs? eventArgs)
    {
        if (eventArgs?.EventId != 14)
            return;
        if (!Sampler.Next())
            return;

        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation($"Starting: {GetOperationName(eventArgs)}");
    }

    // Private methods

    private static string GetOperationName(EventWrittenEventArgs e)
        => e.Payload?[1] as string ?? "n/a";

    // Nested types

    private class MyEventListener : EventListener
    { }
}
