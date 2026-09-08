using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Remaining/total time of a timed state (a location share, a PTT mute);
/// <see cref="Unlimited"/> when it has no expiration.
/// </summary>
public sealed record DurationCountdown(TimeSpan Remaining, TimeSpan Duration)
{
    public static readonly DurationCountdown Unlimited = new(TimeSpan.MaxValue, TimeSpan.MaxValue);
    private static readonly TimeSpan RoundingTolerance = TimeSpan.FromSeconds(2);

    public bool IsUnlimited => Duration == TimeSpan.MaxValue;
    public double Fraction => IsUnlimited ? 1 : Math.Clamp(Remaining / Duration, 0, 1);

    public string GetText(IStringLocalizer l)
    {
        // Bare minutes ("14"): for a ring whose surroundings already say it's a countdown.
        if (IsUnlimited)
            return "";

        if (Remaining.TotalHours >= 1)
            return l.Countdown_Hours_Format((int)Math.Ceiling(Remaining.TotalHours));

        return GetMinutes().ToString();
    }

    public string GetShortText(IStringLocalizer l)
    {
        // Suffixed minutes ("14m"): for a badge next to counters, where a bare number reads as one.
        if (IsUnlimited)
            return "";

        if (Remaining.TotalHours >= 1)
            return l.Countdown_Hours_Format((int)Math.Ceiling(Remaining.TotalHours));

        return l.Countdown_Minutes_Format(GetMinutes());
    }

    // Private methods

    private int GetMinutes()
    {
        var minutes = (int)Math.Ceiling((Remaining - RoundingTolerance).TotalMinutes);
        return Math.Max(1, minutes);
    }
}
