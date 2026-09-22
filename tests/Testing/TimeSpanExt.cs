namespace ActualChat.Testing;

public static class TimeSpanExt
{
    // A build agent runs the whole suite in parallel next to PostgreSQL, Redis, NATS and
    // OpenSearch, so the same wait takes a multiple of its local time there. Waiting too long
    // costs nothing until a test actually fails: a successful wait ends on the event it waits
    // for, not on its budget.
    public static readonly int CiScale = TestRunnerInfo.IsBuildAgent() ? 3 : 1;

    public static TimeSpan Debuggable(this TimeSpan timeSpan)
        => !Debugger.IsAttached ? timeSpan : timeSpan + TimeSpan.FromMinutes(15);

    public static TimeSpan CiScaled(this TimeSpan timeSpan)
        => CiScale == 1 ? timeSpan : timeSpan * CiScale;
}
