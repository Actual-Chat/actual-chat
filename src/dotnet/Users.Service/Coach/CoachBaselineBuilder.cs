using ActualChat.Users.Module;
using ActualLab.Generators;

namespace ActualChat.Users;

public static class CoachBaselineBuilder
{
    public const int MaxSources = 5_000;

    public static CoachBaseline Capture(
        Range<Moment> range, Moment capturedAt, CoachMetricKind kind, string language,
        IEnumerable<CoachRecord> records, CoachScoringSettings settings)
    {
        var selected = Select(range, capturedAt, kind, language, records);
        if (selected.Records.Count > MaxSources)
            throw new InvalidOperationException("Too many baseline sources; choose a shorter period.");

        var history = History(range, kind, language, selected.Records, settings);
        if (history.Value is not { } value)
            throw new InvalidOperationException("Not enough measured speech for a personal baseline.");

        var numerator = kind == CoachMetricKind.Pace ? history.MeasuredWords
            : selected.Records.Sum(r => kind == CoachMetricKind.Fillers
                ? (long)r.Entry!.Fillers + r.Entry.FilledPauses : r.Entry!.WeakWords);
        var denominator = kind == CoachMetricKind.Pace
            ? history.Pace.Summary!.Durations.Values.Sum() : history.MeasuredWords;
        var band = CoachScoring.PaceRange(settings, language);
        return new CoachBaseline {
            Id = RandomStringGenerator.Default.Next(), Language = language, Kind = kind,
            SourceRange = range, CapturedAt = capturedAt, Value = value,
            Numerator = numerator, Denominator = denominator,
            History = history with { Words = [], Pace = history.Pace with { Moments = [] } },
            Sources = selected.Records.Select(r => new CoachBaselineSource(r.SourceId, r.Version)).ToApiArray(),
            Slow = band.Slow, Fast = band.Fast, OmittedSeconds = selected.OmittedSeconds,
        };
    }

    public static CoachBaselineComparison Compare(
        CoachBaseline baseline, Range<Moment> requestedRange, Moment now,
        IEnumerable<CoachRecord> records, CoachScoringSettings settings)
    {
        var band = CoachScoring.PaceRange(settings, baseline.Language);
        var result = new CoachBaselineComparison {
            Baseline = baseline,
            HasRangeChanged = baseline.Kind == CoachMetricKind.Pace
                && (baseline.Slow != band.Slow || baseline.Fast != band.Fast),
        };
        if (baseline.InvalidatedAt is not null || baseline.MeasurementVersion != 1)
            return result;

        var end = Moment.Min(requestedRange.End, now);
        var start = Moment.Min(end, Moment.Max(requestedRange.Start, baseline.CapturedAt));
        var range = new Range<Moment>(start, end);
        var entries = records.ToList();
        var selected = Select(range, now, baseline.Kind, baseline.Language, entries);
        var omitted = entries.Where(r => r.Entry is not null && !r.IsExcluded)
            .Where(r => r.Entry!.Language is { } l && Language.GetIsoCode(l) == baseline.Language)
            .Where(r => r.OccurredAt < start && r.OccurredAt + TimeSpan.FromSeconds(r.Entry!.DurationSeconds) > start)
            .Sum(r => r.Entry!.DurationSeconds);
        var history = History(range, baseline.Kind, baseline.Language, selected.Records, settings);
        return result with {
            History = history, OmittedSeconds = selected.OmittedSeconds + omitted,
            Change = history.Value is { } value ? value - baseline.Value : null,
            IsBetter = CoachProgressBuilder.IsBetter(
                baseline.Kind, baseline.Value, history.Value, settings, baseline.Language),
        };
    }

    private static CoachSkillHistory History(
        Range<Moment> range, CoachMetricKind kind, string language,
        IEnumerable<CoachRecord> records, CoachScoringSettings settings)
    {
        var entries = records.ToList();
        var days = entries.GroupBy(r => r.Day)
            .SelectMany(g => CoachDayBuilder.BuildAll(g.Key, g, settings.MinVocabularyWords));
        var dayRange = new Range<Moment>(UsageDay.DayOf(range.Start), range.End);
        var history = CoachHistoryBuilder.Build(dayRange, kind, days, settings, language) with { Range = range };
        if (kind != CoachMetricKind.Pace)
            return history;

        var band = CoachScoring.PaceRange(settings, language);
        return history with { Pace = CoachPaceDetails.FromRecords(entries, band.Slow, band.Fast) };
    }

    private static (List<CoachRecord> Records, double OmittedSeconds) Select(
        Range<Moment> range, Moment cutoff, CoachMetricKind kind, string language, IEnumerable<CoachRecord> records)
    {
        var matching = records.Where(r => r.Entry is not null && !r.IsExcluded)
            .Where(r => r.OccurredAt >= range.Start && r.OccurredAt < range.End)
            .Where(r => r.Entry!.Language is { } l && Language.GetIsoCode(l) == language)
            .Where(r => kind == CoachMetricKind.Pace ? r.Entry!.Pace is not null : r.Entry!.IsTagged)
            .ToList();
        var omitted = matching.Where(r => r.OccurredAt + TimeSpan.FromSeconds(r.Entry!.DurationSeconds) > cutoff)
            .ToList();
        return (matching.Except(omitted).ToList(), omitted.Sum(r => r.Entry!.DurationSeconds));
    }
}
