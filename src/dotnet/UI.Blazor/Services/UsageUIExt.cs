namespace ActualChat.UI.Blazor.Services;

public static class UsageUIExt
{
    public static void RecordFunnelEvent(this UIHub hub, FunnelEvent funnelEvent)
        // Fire-and-forget: a lost count must never surface in the UI
        => _ = hub.Commander
            .Call(new Usage_RecordFunnelEvent { Session = hub.Session, Event = funnelEvent }, CancellationToken.None)
            .ContinueWith(
                t => hub.Services.LogFor(typeof(UsageUIExt))
                    .LogDebug(t.Exception, "Failed to record funnel event {Event}", funnelEvent),
                TaskContinuationOptions.OnlyOnFaulted);
}
