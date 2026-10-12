using ActualChat.Users.Module;

namespace ActualChat.Users;

public static class CoachProgressBuilder
{
    private const int FiveDayWeekDays = 5;
    private const double RateChangeTolerance = 0.005;
    private const double PaceChangeTolerance = 5;

    public static ApiArray<CoachWeekDelta> WeekDeltas(
        CoachDay thisWeek, CoachDay lastWeek, CoachLanguageLevel level, CoachScoringSettings s, string? language)
    {
        var current = CoachScoring.Summarize(CoachWindow.Days7, thisWeek, null, s, language).Metrics;
        var previous = CoachScoring.Summarize(CoachWindow.Days7, lastWeek, null, s, language).Metrics;
        var kinds = CoachSkillSets.Headline(level)
            .Concat([CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.WeakWords])
            .Concat(CoachSkillSets.Conversation)
            .Distinct();
        return kinds
            .Select(kind => {
                var now = current.FirstOrDefault(m => m.Kind == kind);
                var was = previous.FirstOrDefault(m => m.Kind == kind);
                var nowValue = ComparisonValue(now, thisWeek, s);
                var wasValue = ComparisonValue(was, lastWeek, s);
                var band = nowValue is null ? CoachBand.None : now?.Band ?? CoachBand.None;
                return new CoachWeekDelta(kind, wasValue, nowValue, band,
                    IsBetter(kind, wasValue, nowValue, s, language));
            })
            .ToApiArray();
    }

    public static ApiArray<CoachMilestone> Milestones(
        IReadOnlyList<CoachDay> days, CoachScoringSettings s, string? language)
    {
        var ordered = days.OrderBy(d => d.Day).ToList();
        var total = 0;
        Moment? at1K = null, at10K = null, at100K = null;
        foreach (var d in ordered) {
            total += d.Words;
            if (at1K is null && total >= 1_000)
                at1K = d.Day;
            if (at10K is null && total >= 10_000)
                at10K = d.Day;
            if (at100K is null && total >= 100_000)
                at100K = d.Day;
        }

        var weeks = ordered
            .GroupBy(d => WeekStart(d.Day))
            .OrderBy(g => g.Key)
            .Select(g => (Start: g.Key, Days: g.ToList(), Merged: CoachDayBuilder.Merge(g.Key, g)))
            .ToList();
        var filler = CoachScoring.FillerRange(s, language);
        Moment? fiveDay = weeks.Where(w => w.Days.Count(d => d.Words > 0) >= FiveDayWeekDays)
            .Select(w => (Moment?)w.Start).FirstOrDefault();
        Moment? cleanFiller = weeks
            .Where(w => w.Merged.Words >= s.MinScoreWords && w.Merged.TaggedWords > 0
                && (double)(w.Merged.FilledPauses + w.Merged.Fillers) / w.Merged.TaggedWords < filler.Good)
            .Select(w => (Moment?)w.Start).FirstOrDefault();
        Moment? noLongMonologue = weeks
            .Where(w => w.Merged.Runs > 0 && w.Merged.LongestMonologueSeconds < s.MonologueFlagSeconds)
            .Select(w => (Moment?)w.Start).FirstOrDefault();

        int? previousScore = null;
        Moment? rising = null;
        var months = ordered.GroupBy(d => MonthStart(d.Day)).OrderBy(g => g.Key);
        foreach (var month in months) {
            var score = CoachScoring.Score(CoachDayBuilder.Merge(month.Key, month), s, language);
            if (rising is null && score is { } current && previousScore is { } previous && current > previous)
                rising = month.Key;
            previousScore = score ?? previousScore;
        }
        return ApiArray.New(
            new CoachMilestone(CoachMilestoneKind.Words1K, at1K),
            new CoachMilestone(CoachMilestoneKind.Words10K, at10K),
            new CoachMilestone(CoachMilestoneKind.Words100K, at100K),
            new CoachMilestone(CoachMilestoneKind.FiveDayWeek, fiveDay),
            new CoachMilestone(CoachMilestoneKind.CleanFillerWeek, cleanFiller),
            new CoachMilestone(CoachMilestoneKind.NoLongMonologueWeek, noLongMonologue),
            new CoachMilestone(CoachMilestoneKind.RisingMonth, rising));
    }

    public static ApiArray<CoachWeekScore> WeeklyScores(
        IReadOnlyList<CoachDay> days, int weeks, Moment now, CoachScoringSettings s, string? language)
    {
        var thisWeek = WeekStart(UsageDay.DayOf(now));
        return Enumerable.Range(0, weeks)
            .Select(i => thisWeek - TimeSpan.FromDays(7 * (weeks - 1 - i)))
            .Select(start => {
                var end = start + TimeSpan.FromDays(7);
                var merged = CoachDayBuilder.Merge(start, days.Where(d => d.Day >= start && d.Day < end));
                return new CoachWeekScore(start, CoachScoring.Score(merged, s, language));
            })
            .ToApiArray();
    }

    public static Moment WeekStart(Moment day)
        => CoachWeek.StartOf(day);

    public static bool? IsBetter(
        CoachMetricKind kind, double? was, double? now, CoachScoringSettings s, string? language)
    {
        if (was is not { } before || now is not { } after || Math.Abs(before - after) < 1e-9)
            return null;

        switch (kind) {
        case CoachMetricKind.Fillers or CoachMetricKind.WeakWords:
            return CompareChange(after - before, RateChangeTolerance);
        case CoachMetricKind.Repetition or CoachMetricKind.Profanity
            or CoachMetricKind.Monologue or CoachMetricKind.Interruptions:
            return after < before;
        case CoachMetricKind.Vocabulary or CoachMetricKind.Questions:
            return after > before;
        case CoachMetricKind.Pace:
            var pace = CoachScoring.PaceRange(s, language);
            return CompareDistance(before, after, pace.Slow, pace.Fast, PaceChangeTolerance);
        case CoachMetricKind.TurnTaking:
            return CompareDistance(before, after, s.TurnLowFactor, s.TurnHighFactor);
        case CoachMetricKind.SentenceLength:
            return CompareDistance(before, after, s.SentenceShort, s.SentenceLong);
        case CoachMetricKind.Patience:
            return CompareDistance(before, after, s.PatienceLowSeconds, s.PatienceHighSeconds);
        default:
            return null;
        }
    }

    // Private methods

    private static Moment MonthStart(Moment day)
    {
        var date = day.ToDateTime();
        return new Moment(new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static double? ComparisonValue(CoachMetric? metric, CoachDay day, CoachScoringSettings s)
    {
        if (metric is null || day.Words < s.MinScoreWords)
            return null;

        return metric.Kind switch {
            CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Profanity
                => day.TaggedWords >= s.MinScoreWords ? metric.Rate : null,
            CoachMetricKind.Repetition or CoachMetricKind.TurnTaking or CoachMetricKind.Interruptions
                => metric.Rate,
            _ => metric.Value,
        };
    }

    private static bool? CompareDistance(
        double before, double after, double low, double high,
        double tolerance = 0)
        => CompareChange(Distance(after, low, high) - Distance(before, low, high), tolerance);

    private static bool? CompareChange(double change, double tolerance)
        => Math.Abs(change) < Math.Max(1e-9, tolerance - 1e-9) ? null : change < 0;

    private static double Distance(double value, double low, double high)
        => value < low ? low - value : value > high ? value - high : 0;
}
