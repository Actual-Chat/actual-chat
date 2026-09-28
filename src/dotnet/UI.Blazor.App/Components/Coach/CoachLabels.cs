using ActualChat.Localization;
using ActualChat.Users;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Every piece of coach text that depends on a metric kind, a band or a window; an instance so it
/// can reach the localizer, as the style guide asks of enum-to-text mappings.
/// </summary>
public sealed class CoachLabels(IStringLocalizer l)
{
    public string Window(CoachWindow window)
        => window switch {
            CoachWindow.Today => l.Coach_WindowToday,
            CoachWindow.Week => l.Coach_WindowWeek,
            CoachWindow.Month => l.Coach_WindowMonth,
            _ => l.Coach_WindowAllTime,
        };

    public string MetricTitle(CoachMetricKind kind)
        => kind switch {
            CoachMetricKind.Pace => l.Coach_MetricPace,
            CoachMetricKind.Pauses => l.Coach_MetricPauses,
            CoachMetricKind.Fillers => l.Coach_MetricFillers,
            CoachMetricKind.WeakWords => l.Coach_MetricWeakWords,
            CoachMetricKind.Repetition => l.Coach_MetricRepetition,
            CoachMetricKind.Profanity => l.Coach_MetricProfanity,
            CoachMetricKind.Questions => l.Coach_MetricQuestions,
            CoachMetricKind.SentenceLength => l.Coach_MetricSentenceLength,
            CoachMetricKind.Vocabulary => l.Coach_MetricVocabulary,
            CoachMetricKind.TurnTaking => l.Coach_MetricTurnTaking,
            CoachMetricKind.Patience => l.Coach_MetricPatience,
            CoachMetricKind.Interruptions => l.Coach_MetricInterruptions,
            _ => l.Coach_MetricMonologue,
        };

    // High doubles as fast/long and Low as slow/short, so the label depends on the kind too
    public string Band(CoachMetricKind kind, CoachBand band)
        => (kind, band) switch {
            (_, CoachBand.None) => "",
            (CoachMetricKind.Pace, CoachBand.High) => l.Coach_BandFast,
            (CoachMetricKind.Pace, CoachBand.Low) => l.Coach_BandSlow,
            (CoachMetricKind.SentenceLength, CoachBand.High) => l.Coach_BandLong,
            (CoachMetricKind.SentenceLength, CoachBand.Low) => l.Coach_BandShort,
            (CoachMetricKind.Monologue, CoachBand.High) => l.Coach_BandLong,
            (CoachMetricKind.TurnTaking or CoachMetricKind.Patience, CoachBand.Good) => l.Coach_BandBalanced,
            (_, CoachBand.Good) => l.Coach_BandGood,
            (_, CoachBand.Medium) => l.Coach_BandMedium,
            (_, CoachBand.High) => l.Coach_BandHigh,
            _ => l.Coach_BandLow,
        };

    public string Value(CoachMetric metric)
    {
        if (metric.Value is not { } value)
            return l.Coach_NoData;

        return metric.Kind switch {
            CoachMetricKind.Pace => l.Coach_Wpm_Format(Round(value)),
            CoachMetricKind.Pauses => l.Coach_PerMinute_Format(value.ToString("F1", null)),
            CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
                or CoachMetricKind.Profanity => metric.Rate is null ? l.Coach_NoData : Round(value).ToString(),
            CoachMetricKind.SentenceLength => l.Coach_WordsPerSentence_Format(value.ToString("F1", null)),
            CoachMetricKind.Vocabulary => l.Coach_Percent_Format(Round(value * 100)),
            CoachMetricKind.TurnTaking => l.Coach_PercentOfTalkTime_Format(Round(value * 100)),
            CoachMetricKind.Patience or CoachMetricKind.Monologue => l.Coach_Seconds_Format(value.ToString("F1", null)),
            _ => Round(value).ToString(),
        };
    }

    // No rate means no tagged words yet, so a zero count is absence of data rather than a clean sheet
    public string Rate(CoachMetric metric)
        => metric.Kind is CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
            or CoachMetricKind.Profanity && metric.Rate is { } rate
            ? l.Coach_PercentOfSpeech_Format(Round(rate * 100))
            : "";

    public string PaceNudge(CoachBand band)
        => band switch {
            CoachBand.High => l.Coach_NudgePaceFast,
            CoachBand.Low => l.Coach_NudgePaceSlow,
            _ => l.Coach_NudgePaceGood,
        };

    public string TurnNudge(CoachBand band)
        => band switch {
            CoachBand.Low => l.Coach_NudgeTurnLow,
            CoachBand.High => l.Coach_NudgeTurnHigh,
            _ => l.Coach_NudgeTurnGood,
        };

    private static int Round(double value)
        => (int)Math.Round(value);
}
