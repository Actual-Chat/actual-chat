using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

// Per-signal hysteresis counter shared by Sender/Receiver classifiers.
// One instance per classifier signal; Update() bumps the matching streak and
// promotes to Bad/Good once the required streak length is reached.
internal sealed class HealthStreakState
{
    public int BadStreak;
    public int GoodStreak;
    public HealthVerdict Current = HealthVerdict.Unknown;

    public HealthVerdict Update(int badStreakRequired, int goodStreakRequired, bool isBad, bool isGood)
    {
        if (isBad) {
            BadStreak++;
            GoodStreak = 0;
            if (BadStreak >= badStreakRequired) Current = HealthVerdict.Bad;
            else if (Current == HealthVerdict.Unknown) Current = HealthVerdict.Marginal;
        }
        else if (isGood) {
            GoodStreak++;
            BadStreak = 0;
            if (GoodStreak >= goodStreakRequired) Current = HealthVerdict.Good;
            else if (Current == HealthVerdict.Unknown) Current = HealthVerdict.Marginal;
        }
        else {
            BadStreak = 0;
            GoodStreak = 0;
            if (Current == HealthVerdict.Unknown) Current = HealthVerdict.Marginal;
        }
        return Current;
    }
}

internal static class HealthVerdictExt
{
    public static HealthVerdict Combine(params ReadOnlySpan<HealthVerdict> verdicts)
    {
        var worst = HealthVerdict.Unknown;
        foreach (var v in verdicts) {
            if (v == HealthVerdict.Unknown) continue;
            if (worst == HealthVerdict.Unknown || (int)v > (int)worst)
                worst = v;
        }
        return worst;
    }
}
