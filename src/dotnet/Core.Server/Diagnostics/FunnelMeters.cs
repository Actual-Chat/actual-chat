using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

public static class FunnelMeters
{
    public static readonly Counter<long> Events;

    static FunnelMeters()
    {
        var m = CoreServerInstruments.Meter;
        Events = m.CreateCounter<long>(
            "usage.funnel.events", null, "Growth funnel steps, by event and app");
    }

    public static void Record(FunnelEvent funnelEvent, AppKind appKind, ArrivalKind? arrivalKind = null)
    {
        var tags = new TagList {
            { "event", funnelEvent.ToString() },
            { "app", appKind.ToString() },
        };
        if (arrivalKind is { } vArrivalKind)
            tags.Add("arrival", vArrivalKind.ToString());
        Events.Add(1, tags);
    }
}
