namespace ActualChat.UI.Blazor.App.Services;

public static class WalkieTalkie
{
    public static bool IsStaleWake(Moment startedAt, Moment now)
        => now - startedAt > Constants.Audio.WalkieTalkieStaleWakeAge;

    public static Moment? ComputeIdleDropAt(
        bool hasAnyActivity, Moment? lastActiveAt, Moment idleSince, TimeSpan idleTimeout)
    {
        // Activity is a level (LiveStreamUI.HasActivity), so the caller stamps lastActiveAt on
        // the observed active->idle edge; idleSince clamps a stamp leaked from a prior session.
        if (hasAnyActivity)
            return null;

        return Moment.Max(idleSince, lastActiveAt ?? idleSince) + idleTimeout;
    }
}
