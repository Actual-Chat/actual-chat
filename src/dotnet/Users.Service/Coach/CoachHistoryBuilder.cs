using ActualChat.Users.Module;

namespace ActualChat.Users;

public static class CoachHistoryBuilder
{
    public static CoachSkillHistory Build(
        Range<Moment> range, CoachMetricKind kind, IEnumerable<CoachDay> days,
        CoachScoringSettings settings, string language)
    {
        if (kind is not (CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Pace))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var perDay = days
            .Where(d => d.Day >= range.Start && d.Day < range.End)
            .Where(d => d.Language == language)
            .GroupBy(d => d.Day)
            .OrderBy(g => g.Key)
            .Select(g => CoachDayBuilder.Merge(g.Key, g))
            .ToList();
        var merged = CoachDayBuilder.Merge(range.Start, perDay);
        var summary = CoachScoring.Summarize(CoachWindow.AllTime, merged, null, settings, language);
        var points = new List<CoachSkillDay>();
        Moment? previous = null;
        foreach (var day in perDay) {
            var (value, words, seconds) = Measure(day, kind, settings, language);
            if (value is null)
                continue;

            var gap = previous is { } p ? Math.Max(0, (int)(day.Day - p).TotalDays - 1) : 0;
            points.Add(new CoachSkillDay(day.Day, value.Value, words, seconds, gap));
            previous = day.Day;
        }
        var (totalValue, totalWords, totalSeconds) = Measure(merged, kind, settings, language);
        var counts = kind == CoachMetricKind.Fillers ? merged.FillerCounts : merged.WeakWordCounts;
        return new CoachSkillHistory {
            Range = range,
            Summary = summary,
            Days = points.ToApiArray(),
            Value = totalValue,
            MeasuredWords = totalWords,
            MeasuredSeconds = totalSeconds,
            MinimumWords = settings.MinScoreWords,
            SpeakingDays = perDay.Count(d => kind == CoachMetricKind.Pace
                ? d.Pace?.MeasuredEntries > 0 : d.TaggedWords > 0),
            Words = kind == CoachMetricKind.Pace ? ApiArray<CoachChip>.Empty : counts
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key)
                .Select(p => new CoachChip(p.Key, p.Value))
                .ToApiArray(),
        };
    }

    private static (double? Value, long Words, double Seconds) Measure(
        CoachDay day, CoachMetricKind kind, CoachScoringSettings settings, string language)
    {
        if (kind == CoachMetricKind.Pace) {
            var words = day.Pace is { } pace ? pace.ValidWords - pace.UnclassifiedWords : 0;
            var milliseconds = day.Pace?.Durations.Values.Sum() ?? 0;
            double? value = words >= settings.MinScoreWords && milliseconds > 0
                ? 60_000d * words / milliseconds
                : null;
            return (value, words, milliseconds / 1_000d);
        }
        var metric = CoachScoring.Summarize(CoachWindow.AllTime, day, null, settings, language)
            .Metrics.First(m => m.Kind == kind);
        return (day.TaggedWords >= settings.MinScoreWords ? metric.Rate : null, day.TaggedWords, day.SpeechSeconds);
    }
}
