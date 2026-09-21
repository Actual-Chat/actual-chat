using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;

namespace ActualChat.Users;

public static class UsageMeters
{
    public static readonly Counter<long> EventsRecorded;
    public static readonly Counter<long> EventsSkipped;

    static UsageMeters()
    {
        var m = CoreServerInstruments.Meter;
        EventsRecorded = m.CreateCounter<long>(
            "usage.events.recorded", null, "Usage events written, by kind");
        EventsSkipped = m.CreateCounter<long>(
            "usage.events.skipped", null, "Usage events already recorded, by kind");
    }
}
