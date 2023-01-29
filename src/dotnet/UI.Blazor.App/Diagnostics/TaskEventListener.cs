using System.Diagnostics.Tracing;

namespace ActualChat.UI.Blazor.App.Diagnostics;

// Requires <TrimmerRootAssembly Include="System.Private.CoreLib" />
public class TaskEventListener : WorkerBase
{
    private readonly long SummaryInterval = TimeSpan.FromSeconds(3).Ticks;
    private const int SampleRatioMask = 127;

    private int _eventCount;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public TaskEventListener(IServiceProvider services) : base()
    {
        Services = services;
        Log = services.LogFor(GetType());
    }
    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var tEventSource = Type.GetType("System.Threading.Tasks.TplEventSource")!;
        var fLog = tEventSource.GetField("Log", BindingFlags.Static | BindingFlags.Public)!;
        var eventSource = (fLog.GetValue(null) as EventSource)!;

        var listener = new MyEventListener();
        listener.EventWritten += OnEvent;
        listener.EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)8);

        Log.LogInformation("Started, IsEnabled: {IsEnabled}", eventSource.IsEnabled(EventLevel.Informational, (EventKeywords)8));

        await Task.Delay(TimeSpan.FromDays(365), cancellationToken).ConfigureAwait(false);
    }

    private void OnEvent(object? source, EventWrittenEventArgs? eventArgs)
    {
        if (eventArgs?.EventId != 14)
            return;

        var eventCount = Interlocked.Increment(ref _eventCount);
        if ((eventCount & SampleRatioMask) != 0)
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
