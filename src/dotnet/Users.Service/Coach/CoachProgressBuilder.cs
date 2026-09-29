using ActualChat.Users.Module;

namespace ActualChat.Users;

public static class CoachProgressBuilder
{
    private const int FiveDayWeekDays = 5;

    public static ApiArray<CoachWeekDelta> WeekDeltas(
        CoachDay thisWeek, CoachDay lastWeek, CoachLanguageLevel level, CoachScoringSettings s, string? language)
    {
        var current = CoachScoring.Summarize(CoachWindow.Days7, thisWeek, null, s, language).Metrics;
        var previous = lastWeek.Words >= s.MinScoreWords
            ? CoachScoring.Summarize(CoachWindow.Days7, lastWeek, null, s, language).Metrics
            : (ApiArray<CoachMetric>?)null;
        var kinds = CoachSkillSets.Headline(level).Concat(CoachSkillSets.Conversation).Distinct();
        return kinds
            .Select(kind => {
                var now = current.FirstOrDefault(m => m.Kind == kind);
                var was = previous?.FirstOrDefault(m => m.Kind == kind);
                var nowValue = now is null ? null : now.Rate ?? now.Value;
                var wasValue = was is null ? null : was.Rate ?? was.Value;
                return new CoachWeekDelta(kind, wasValue, nowValue, now?.Band ?? CoachBand.None,
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

    // Private methods

    private static Moment MonthStart(Moment day)
    {
        var date = day.ToDateTime();
        return new Moment(new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static bool? IsBetter(CoachMetricKind kind, double? was, double? now, CoachScoringSettings s, string? language)
    {
        if (was is not { } before || now is not { } after || Math.Abs(before - after) < 1e-9)
            return null;

        switch (kind) {
        case CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
            or CoachMetricKind.Profanity or CoachMetricKind.Monologue or CoachMetricKind.Interruptions:
            return after < before;
        case CoachMetricKind.Vocabulary or CoachMetricKind.Questions:
            return after > before;
        case CoachMetricKind.Pace:
            var pace = CoachScoring.PaceRange(s, language);
            return Distance(after, pace.Slow, pace.Fast) < Distance(before, pace.Slow, pace.Fast);
        case CoachMetricKind.TurnTaking:
            return Distance(after, s.TurnLowFactor, s.TurnHighFactor)
                < Distance(before, s.TurnLowFactor, s.TurnHighFactor);
        case CoachMetricKind.SentenceLength:
            return Distance(after, s.SentenceShort, s.SentenceLong)
                < Distance(before, s.SentenceShort, s.SentenceLong);
        case CoachMetricKind.Patience:
            return Distance(after, s.PatienceLowSeconds, s.PatienceHighSeconds)
                < Distance(before, s.PatienceLowSeconds, s.PatienceHighSeconds);
        default:
            return null;
        }
    }

    private static double Distance(double value, double low, double high)
        => value < low ? low - value : value > high ? value - high : 0;
}
