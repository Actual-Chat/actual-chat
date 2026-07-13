namespace ActualChat.UI.Blazor.App.Services;

public static class WalkieTalkie
{
    public static bool IsStaleWake(Moment startedAt, Moment now)
        => now - startedAt > Constants.Audio.WalkieTalkieStaleWakeAge;

    public static Moment? ComputeIdleDropAt(
        IReadOnlyList<Moment?> lastActivityTimes, Moment idleSince, TimeSpan idleTimeout)
    {
        // A null last-activity means the chat is streaming right now (see
        // LiveStreamUI.GetLastActivityServerTime), so there is no drop time at all.
        var lastActivity = idleSince;
        foreach (var t in lastActivityTimes) {
            if (t is null)
                return null;

            lastActivity = Moment.Max(lastActivity, t.Value);
        }
        return lastActivity + idleTimeout;
    }
}
