using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// Bands, sub-scores and the 0-100 score over a merged day; the same bands serve the tab, the
/// trends and the tip rules.
/// </summary>
public static class CoachScoring
{
    private const int MaxChips = 5;

    public static CoachSummary Summarize(
        CoachWindow window, CoachDay d, CoachDay? trailing, CoachScoringSettings s, string? language = null)
    {
        var score = Score(d, s, language);
        int? delta = null;
        if (score is { } current && trailing is not null && Score(trailing, s, language) is { } previous) {
            var diff = current - previous;
            delta = Math.Abs(diff) >= s.BadgeMinDelta ? diff : null;
        }
        return new CoachSummary(window, score, delta, d.Words, d.Entries, d.TaggedEntries, Metrics(d, s, language));
    }

    public static int? Score(CoachDay d, CoachScoringSettings s, string? language)
    {
        if (d.Words < s.MinScoreWords)
            return null;

        var band = Band(s, language);
        var weighted = 0d;
        var weights = 0;
        Add(FillerRate(d), 0, FillerRange(s, language).Good, s.WeightFillers);
        Add(Pace(d), band.Slow, band.Fast, s.WeightPace);
        Add(WeakRate(d), 0, s.WeakGoodRate, s.WeightWeakWords);
        Add(TurnRatio(d), s.TurnLowFactor, s.TurnHighFactor, s.WeightTurnTaking);
        Add(SentenceLength(d), s.SentenceShort, s.SentenceLong, s.WeightSentenceLength);
        return weights == 0 ? null : (int)Math.Round(weighted / weights);

        void Add(double? value, double low, double high, int weight) {
            if (value is null)
                return;

            weighted += SubScore(value.Value, low, high) * weight;
            weights += weight;
        }
    }

    // 100 inside [goodLow, goodHigh], 0 once the value is a full band width (or, for a band starting
    // at 0, twice the edge) past an edge, linear in between
    public static double SubScore(double value, double goodLow, double goodHigh)
    {
        if (value >= goodLow && value <= goodHigh)
            return 100;

        var fallOff = goodLow <= 0 ? 2 * goodHigh : goodHigh - goodLow;
        var distance = value < goodLow ? goodLow - value : value - goodHigh;
        return Math.Clamp(100 * (1 - distance / fallOff), 0, 100);
    }

    public static CoachBand PaceBand(double wpm, CoachScoringSettings s, string? language)
    {
        var band = Band(s, language);
        return wpm < band.Slow ? CoachBand.Low : wpm > band.Fast ? CoachBand.High : CoachBand.Good;
    }

    public static PaceBand PaceRange(CoachScoringSettings s, string? language)
        => Band(s, language);

    public static RateBand FillerRange(CoachScoringSettings s, string? language)
    {
        var iso = language.IsNullOrEmpty() ? "" : Language.GetIsoCode(language);
        return s.FillerByLanguage.TryGetValue(iso, out var band)
            ? band
            : new RateBand { Good = s.FillerGoodRate, High = s.FillerHighRate };
    }

    public static ApiArray<CoachScorePart> Explain(CoachDay d, CoachScoringSettings s, string? language)
    {
        if (d.Words < s.MinScoreWords)
            return ApiArray<CoachScorePart>.Empty;

        var pace = Band(s, language);
        var filler = FillerRange(s, language);
        var fillerRate = FillerRate(d);
        var paceValue = Pace(d);
        var raw = new List<(CoachMetricKind Kind, double? Value, double Low, double High, int Weight, CoachBand Band)> {
            (CoachMetricKind.Fillers, fillerRate, 0, filler.Good, s.WeightFillers,
                RateBand(fillerRate, filler.Good, filler.High)),
            (CoachMetricKind.Pace, paceValue, pace.Slow, pace.Fast, s.WeightPace,
                paceValue is { } p ? PaceBand(p, s, language) : CoachBand.None),
            (CoachMetricKind.WeakWords, WeakRate(d), 0, s.WeakGoodRate, s.WeightWeakWords,
                RateBand(WeakRate(d), s.WeakGoodRate, s.WeakHighRate)),
            (CoachMetricKind.TurnTaking, TurnRatio(d), s.TurnLowFactor, s.TurnHighFactor, s.WeightTurnTaking,
                RangeBand(TurnRatio(d), s.TurnLowFactor, s.TurnHighFactor)),
            (CoachMetricKind.SentenceLength, SentenceLength(d), s.SentenceShort, s.SentenceLong,
                s.WeightSentenceLength, RangeBand(SentenceLength(d), s.SentenceShort, s.SentenceLong)),
        };
        var present = raw.Where(x => x.Value is not null).ToList();
        var totalWeight = present.Sum(x => x.Weight);
        if (totalWeight == 0)
            return ApiArray<CoachScorePart>.Empty;

        return present
            .Select(x => {
                var max = 100d * x.Weight / totalWeight;
                return new CoachScorePart(
                    x.Kind, x.Weight, x.Band, SubScore(x.Value!.Value, x.Low, x.High) / 100 * max, max);
            })
            .OrderBy(x => x.Points - x.MaxPoints)
            .ToApiArray();
    }

    // Private methods

    private static ApiArray<CoachMetric> Metrics(CoachDay d, CoachScoringSettings s, string? language)
    {
        var pace = Pace(d);
        var fillerRate = FillerRate(d);
        var weakRate = WeakRate(d);
        var repetitionRate = d.Words > 0 ? (double)d.Repetitions / d.Words : (double?)null;
        var sentence = SentenceLength(d);
        var turn = TurnRatio(d);
        var patience = d.Responses > 0 ? d.ResponseGapSeconds / d.Responses : (double?)null;
        var speechMinutes = d.SpeechSeconds / 60;
        var none = ApiArray<CoachChip>.Empty;
        return ApiArray.New(
            new CoachMetric(CoachMetricKind.Pace, pace, null,
                pace is { } p ? PaceBand(p, s, language) : CoachBand.None, none),
            new CoachMetric(CoachMetricKind.Pauses, speechMinutes > 0 ? d.Pauses / speechMinutes : null, null,
                CoachBand.None, none),
            new CoachMetric(CoachMetricKind.Fillers, d.FilledPauses + d.Fillers, fillerRate,
                RateBand(fillerRate, FillerRange(s, language).Good, FillerRange(s, language).High),
                Chips(d.FillerCounts)),
            new CoachMetric(CoachMetricKind.WeakWords, d.WeakWords, weakRate,
                RateBand(weakRate, s.WeakGoodRate, s.WeakHighRate), Chips(d.WeakWordCounts)),
            new CoachMetric(CoachMetricKind.Repetition, d.Repetitions, repetitionRate,
                repetitionRate is { } r
                    ? (r < s.RepetitionGoodRate ? CoachBand.Good : CoachBand.High)
                    : CoachBand.None,
                none),
            new CoachMetric(CoachMetricKind.Profanity, d.Profanities,
                d.TaggedWords > 0 ? (double)d.Profanities / d.TaggedWords : null, CoachBand.None, none),
            new CoachMetric(CoachMetricKind.Questions, d.Questions, null, CoachBand.None, none),
            new CoachMetric(CoachMetricKind.SentenceLength, sentence, null,
                RangeBand(sentence, s.SentenceShort, s.SentenceLong), none),
            new CoachMetric(CoachMetricKind.Vocabulary,
                d.VocabularyWords > 0 ? d.VocabularyDistinct / d.VocabularyWords : null, null, CoachBand.None, none),
            new CoachMetric(CoachMetricKind.TurnTaking,
                d.TotalSpeechSeconds > 0 ? d.OwnSpeechSeconds / d.TotalSpeechSeconds : null, turn,
                RangeBand(turn, s.TurnLowFactor, s.TurnHighFactor), none),
            new CoachMetric(CoachMetricKind.Patience, patience, null,
                RangeBand(patience, s.PatienceLowSeconds, s.PatienceHighSeconds), none),
            new CoachMetric(CoachMetricKind.Interruptions, d.Interruptions,
                d.OwnTurns > 0 ? (double)d.Interruptions / d.OwnTurns : null, CoachBand.None, none),
            new CoachMetric(CoachMetricKind.Monologue, d.Runs > 0 ? d.LongestMonologueSeconds : null, null,
                d.Runs > 0
                    ? (d.LongestMonologueSeconds >= s.MonologueFlagSeconds ? CoachBand.High : CoachBand.Good)
                    : CoachBand.None,
                none));
    }

    private static CoachBand RateBand(double? rate, double good, double high)
        => rate is { } r ? (r < good ? CoachBand.Good : r <= high ? CoachBand.Medium : CoachBand.High) : CoachBand.None;

    private static CoachBand RangeBand(double? value, double low, double high)
        => value is { } v ? (v < low ? CoachBand.Low : v > high ? CoachBand.High : CoachBand.Good) : CoachBand.None;

    private static ApiArray<CoachChip> Chips(ApiMap<string, int> counts)
        => counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(MaxChips)
            .Select(x => new CoachChip(x.Key, x.Value))
            .ToApiArray();

    private static double? Pace(CoachDay d)
        => d.SpeechSeconds > 0 && d.Words > 0 ? d.Words * 60 / d.SpeechSeconds : null;

    private static double? FillerRate(CoachDay d)
        => d.TaggedWords > 0 ? (double)(d.FilledPauses + d.Fillers) / d.TaggedWords : null;

    private static double? WeakRate(CoachDay d)
        => d.TaggedWords > 0 ? (double)d.WeakWords / d.TaggedWords : null;

    private static double? SentenceLength(CoachDay d)
        => d.Sentences > 0 ? (double)d.Words / d.Sentences : null;

    private static double? TurnRatio(CoachDay d)
        => d.FairShareSeconds > 0 ? d.OwnSpeechSeconds / d.FairShareSeconds : null;

    private static PaceBand Band(CoachScoringSettings s, string? language)
    {
        var iso = language is null ? "" : Language.GetIsoCode(language);
        return s.PaceByLanguage.TryGetValue(iso, out var band)
            ? band
            : new PaceBand { Slow = s.PaceSlowWpm, Fast = s.PaceFastWpm };
    }
}
