using System.Diagnostics.Metrics;

namespace ActualChat;

public static class Tracer
{
    public static ActivitySource Span { get; }
        = new ActivitySource("ActualChat.App", ThisAssembly.AssemblyInformationalVersion);

    public static Meter Metric { get; }
        = new Meter("ActualChat.App", ThisAssembly.AssemblyInformationalVersion);

    public static Activity? StartActivity(
        ActivityKind kind = ActivityKind.Internal,
        [CallerMemberName] string? activityName = null)
    {
        if (string.IsNullOrEmpty(activityName))
            return null;

        if (activityName.Length >= 6 && activityName[^5..] == "Async")
            activityName = activityName[..^5];

        return Span.StartActivity(activityName, kind);
    }

    public static ActivityEvent CreateEvent(
        DateTimeOffset timestamp = default,
        ActivityTagsCollection? tags = null,
        [CallerMemberName] string? eventName = null)
    {
        if (string.IsNullOrEmpty(eventName))
            return new ActivityEvent();

        if (eventName.Length >= 6 && eventName[^5..] == "Async")
            eventName = eventName[..^5];

        return new ActivityEvent(eventName, timestamp, tags);
    }
}
