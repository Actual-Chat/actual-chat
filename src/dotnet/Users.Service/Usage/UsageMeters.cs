using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;

namespace ActualChat.Users;

public static class UsageMeters
{
    public static readonly Counter<long> EventsRecorded;
    public static readonly Counter<long> EventsSkipped;
    public static readonly Counter<long> ReviewPromptVerdicts;
    public static readonly Counter<long> ReviewPromptOutcomes;

    static UsageMeters()
    {
        var m = CoreServerInstruments.Meter;
        EventsRecorded = m.CreateCounter<long>(
            "usage.events.recorded", null, "Usage events written, by kind");
        EventsSkipped = m.CreateCounter<long>(
            "usage.events.skipped", null, "Usage events already recorded, by kind");
        ReviewPromptVerdicts = m.CreateCounter<long>(
            "usage.review_prompt.verdicts", null, "Review prompt eligibility checks, by verdict");
        ReviewPromptOutcomes = m.CreateCounter<long>(
            "usage.review_prompt.outcomes", null, "Review prompt results, by outcome and app");
    }
}
