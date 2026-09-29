using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Services;
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

    public string Level(CoachLanguageLevel level)
        => level switch {
            CoachLanguageLevel.Native => l.Coach_LevelNative,
            CoachLanguageLevel.Learning => l.Coach_LevelLearning,
            _ => l.Coach_LevelOff,
        };

    public string Tab(CoachTab tab)
        => tab switch {
            CoachTab.Recent => l.Coach_TabRecent,
            CoachTab.Progress => l.Coach_TabProgress,
            _ => l.Coach_TabSkills,
        };

    public string LanguageName(string iso)
        => Languages.All.FirstOrDefault(x => x.IsoCode == iso)?.Title ?? iso;

    public string FocusHint(CoachMetricKind kind, CoachChip? top)
        => kind switch {
            CoachMetricKind.Fillers when top is not null => l.Coach_FocusHintFillers_Format(top.Word, top.Count),
            CoachMetricKind.WeakWords when top is not null => l.Coach_FocusHintWeakWords_Format(top.Word, top.Count),
            CoachMetricKind.Pace => l.Coach_FocusHintPace,
            CoachMetricKind.TurnTaking => l.Coach_FocusHintTurnTaking,
            CoachMetricKind.Monologue => l.Coach_FocusHintMonologue,
            CoachMetricKind.Vocabulary => l.Coach_FocusHintVocabulary,
            CoachMetricKind.SentenceLength => l.Coach_FocusHintSentenceLength,
            _ => "",
        };

    public string Finding(CoachConversation c, CoachFinding f, bool isWeeksBest)
        => f.Kind switch {
            CoachMetricKind.Fillers => c.Fillers <= 2 && c.SpeechSeconds >= 60
                ? l.Coach_FindingFillersFew_Format(c.Fillers, Round(c.SpeechSeconds / 60))
                    + (isWeeksBest ? ". " + l.Coach_BestThisWeek : "")
                : l.Coach_FindingFillers_Format(
                    c.Fillers, Round(100d * c.Fillers / Math.Max(1, c.Words)), TopWord(c.FillerCounts)),
            CoachMetricKind.Pace => l.Coach_FindingPace_Format(Round(c.Pace ?? 0), f.Band switch {
                CoachBand.Low => l.Coach_PaceALittleSlow,
                CoachBand.High => l.Coach_PaceALittleFast,
                _ => l.Coach_PaceComfortableWord,
            }),
            CoachMetricKind.TurnTaking => f.Band switch {
                CoachBand.High => l.Coach_FindingTalkShareHigh_Format(Round((c.TalkShare ?? 0) * 100), c.Participants),
                CoachBand.Low => l.Coach_FindingTalkShareLow_Format(Round((c.TalkShare ?? 0) * 100), c.Participants),
                _ => l.Coach_FindingTalkShareBalanced_Format(Round((c.TalkShare ?? 0) * 100), c.Participants),
            },
            _ => f.Band == CoachBand.High
                ? l.Coach_FindingMonologueLong_Format(Clock(c.LongestMonologueSeconds ?? 0))
                : l.Coach_FindingMonologueShort_Format(Clock(c.LongestMonologueSeconds ?? 0)),
        };

    public string Explain(CoachMetricKind kind, string? language, CoachSummary s)
        => kind switch {
            CoachMetricKind.Fillers => l.Coach_ExplainFillers_Format(Examples(s, kind), Round(s.FillerGood * 100)),
            CoachMetricKind.Pace => l.Coach_ExplainPace_Format(
                Round(s.PaceSlow), Round(s.PaceFast), LanguageName(language ?? "")),
            CoachMetricKind.TurnTaking => l.Coach_ExplainTalkShare,
            CoachMetricKind.Monologue => l.Coach_ExplainMonologue,
            CoachMetricKind.WeakWords => l.Coach_ExplainWeakWords_Format(Examples(s, kind)),
            CoachMetricKind.Vocabulary => l.Coach_ExplainVocabulary,
            CoachMetricKind.SentenceLength => l.Coach_ExplainSentence,
            _ => "",
        };

    public string BandWord(CoachMetricKind kind, CoachBand band)
        => (kind, band) switch {
            (_, CoachBand.None) => "",
            (CoachMetricKind.Fillers or CoachMetricKind.WeakWords, CoachBand.Good) => l.Coach_BandNatural,
            (CoachMetricKind.Fillers or CoachMetricKind.WeakWords, CoachBand.Medium) => l.Coach_BandABitHigh,
            (CoachMetricKind.Pace, CoachBand.Good) => l.Coach_PaceComfortableWord,
            (CoachMetricKind.Pace, CoachBand.Low) => l.Coach_PaceALittleSlow,
            (CoachMetricKind.Pace, CoachBand.High) => l.Coach_PaceALittleFast,
            (CoachMetricKind.TurnTaking, CoachBand.High) => l.Coach_BandABitMuch,
            (CoachMetricKind.Monologue, CoachBand.Good) => l.Coach_BandFine,
            (CoachMetricKind.SentenceLength, CoachBand.Good) => l.Coach_BandClear,
            _ => Band(kind, band),
        };

    // The top chips of the metric, quoted, so the caption names the user's own words
    private static string Examples(CoachSummary s, CoachMetricKind kind)
        => string.Join(", ", (s.Metrics.FirstOrDefault(m => m.Kind == kind)?.Chips ?? ApiArray<CoachChip>.Empty)
            .Take(3)
            .Select(c => $"“{c.Word}”"));

    public string NoteCaption(UserCoachWeeklyNote note)
    {
        var parts = new List<string>();
        if (note.ScoreDelta is { } delta)
            parts.Add(l.Coach_WeekNoteScore_Format(delta > 0 ? "+" + delta : delta.ToString()));
        if (note.FocusKind is { } kind && note.FocusDelta is { } focusDelta)
            parts.Add(l.Coach_WeekNoteFocus_Format(MetricTitle(kind), focusDelta > 0 ? "▲" : "▼"));
        if (note.BestStartLid != 0)
            parts.Add(l.Coach_WeekNoteBest);
        return string.Join(" · ", parts);
    }

    public string Milestone(CoachMilestoneKind kind)
        => kind switch {
            CoachMilestoneKind.Words1K => l.Coach_MilestoneWords1K,
            CoachMilestoneKind.Words10K => l.Coach_MilestoneWords10K,
            CoachMilestoneKind.Words100K => l.Coach_MilestoneWords100K,
            CoachMilestoneKind.FiveDayWeek => l.Coach_MilestoneFiveDayWeek,
            CoachMilestoneKind.CleanFillerWeek => l.Coach_MilestoneCleanFillerWeek,
            CoachMilestoneKind.NoLongMonologueWeek => l.Coach_MilestoneNoLongMonologueWeek,
            _ => l.Coach_MilestoneRisingMonth,
        };

    public string DeltaValue(CoachWeekDelta d)
    {
        if (d.Previous is not { } was || d.Current is not { } now)
            return l.Coach_NotEnoughSpeech;

        return d.Kind switch {
            CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
                => $"{Round(was * 100)}% → " + l.Coach_PercentOfSpeech_Format(Round(now * 100)),
            CoachMetricKind.TurnTaking => $"{Round(was * 100)}% → " + l.Coach_PercentOfTalkTime_Format(Round(now * 100)),
            CoachMetricKind.Pace => $"{Round(was)} → " + l.Coach_Wpm_Format(Round(now)),
            CoachMetricKind.Monologue => $"{Clock(was)} → {Clock(now)}",
            CoachMetricKind.Vocabulary => $"{Round(was * 100)} → " + l.Coach_OfEvery100_Format(Round(now * 100)),
            CoachMetricKind.SentenceLength => $"{was.ToString("F1", null)} → "
                + l.Coach_WordsPerSentence_Format(now.ToString("F1", null)),
            _ => $"{Round(was)} → {Round(now)}",
        };
    }

    public string DeltaBadge(CoachWeekDelta d)
    {
        if (d.Previous is not { } was || d.Current is not { } now || d.IsBetter is null)
            return l.Coach_DeltaSame;

        var arrow = now < was ? "▼" : "▲";
        return d.Kind switch {
            CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
                => was > 0 ? $"{arrow} {Round(Math.Abs(now - was) / was * 100)}%" : arrow,
            CoachMetricKind.TurnTaking => $"{arrow} {Round(Math.Abs(now - was) * 100)}",
            CoachMetricKind.Monologue => $"{arrow} {Clock(Math.Abs(now - was))}",
            _ => $"{arrow} {Round(Math.Abs(now - was))}",
        };
    }

    public string BetterCaption(CoachWeekDelta best, int speakingDays)
    {
        var relative = best.Previous is { } was and > 0 && best.Current is { } now
            ? Round(Math.Abs(now - was) / was * 100)
            : 0;
        return l.Coach_BetterCaption_Format(MetricTitle(best.Kind), relative, speakingDays);
    }

    public static string Clock(double seconds)
        => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    private static string TopWord(ApiMap<string, int> counts)
        => counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).FirstOrDefault().Key ?? "";

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
            CoachMetricKind.Vocabulary => l.Coach_MetricDifferentWords,
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
            CoachMetricKind.Vocabulary => l.Coach_OfEvery100_Format(Round(value * 100)),
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
