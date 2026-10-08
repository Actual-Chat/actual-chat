using ActualChat.Chat;

namespace ActualChat.Users;

/// <summary>
/// One user's speech-coach numbers for one UTC day, rebuilt from the log; windows are merges of days.
/// Sums throughout, except the monologue (a max); the response gap is a sum, so mean = gap / responses.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CoachDay([property: DataMember, Key(0)] Moment Day)
{
    public static readonly CoachDay None = new (Moment.EpochStart);

    [DataMember, Key(1)] public int Entries { get; init; }
    [DataMember, Key(2)] public int TaggedEntries { get; init; }
    [DataMember, Key(3)] public int Words { get; init; }
    [DataMember, Key(4)] public int Sentences { get; init; }
    [DataMember, Key(5)] public int Questions { get; init; }
    [DataMember, Key(6)] public int Repetitions { get; init; }
    [DataMember, Key(7)] public int Pauses { get; init; }
    [DataMember, Key(8)] public int FilledPauses { get; init; }
    [DataMember, Key(9)] public int Fillers { get; init; }
    [DataMember, Key(10)] public int WeakWords { get; init; }
    [DataMember, Key(11)] public int Profanities { get; init; }
    [DataMember, Key(12)] public int Runs { get; init; }
    [DataMember, Key(13)] public int OwnTurns { get; init; }
    [DataMember, Key(14)] public int TotalTurns { get; init; }
    [DataMember, Key(15)] public int Responses { get; init; }
    [DataMember, Key(16)] public int Interruptions { get; init; }
    [DataMember, Key(17)] public double DurationSeconds { get; init; }
    [DataMember, Key(18)] public double SpeechSeconds { get; init; }
    [DataMember, Key(19)] public double PauseSeconds { get; init; }
    [DataMember, Key(20)] public double VocabularyWords { get; init; }
    [DataMember, Key(21)] public double VocabularyDistinct { get; init; }
    [DataMember, Key(22)] public double OwnSpeechSeconds { get; init; }
    [DataMember, Key(23)] public double TotalSpeechSeconds { get; init; }
    [DataMember, Key(24)] public double FairShareSeconds { get; init; }
    [DataMember, Key(25)] public double LongestMonologueSeconds { get; init; }
    [DataMember, Key(26)] public double ResponseGapSeconds { get; init; }
    [DataMember, Key(27)] public ApiMap<string, int> FillerCounts { get; init; } = new ();
    [DataMember, Key(28)] public ApiMap<string, int> WeakWordCounts { get; init; } = new ();
    [DataMember, Key(29)] public int TaggedWords { get; init; }
    // ISO code of the entries the row sums; "" for entries without a language and for runs alone
    [DataMember, Key(30)] public string Language { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [DataMember, Key(31)] public SpeechPaceSummary? Pace { get; init; }
}

public static class CoachDayBuilder
{
    public static ApiArray<CoachDay> BuildAll(Moment day, IEnumerable<CoachRecord> records, int minVocabularyWords)
    {
        var list = records.Where(r => r.Day == day).ToList();
        var runs = list.Where(r => r.Run is not null).ToList();
        var byLanguage = list
            .Where(r => r.Entry is not null)
            .GroupBy(r => r.Entry!.Language is { } l ? ActualChat.Language.GetIsoCode(l) : "")
            .ToDictionary(g => g.Key, g => g.ToList());
        if (byLanguage.Count == 0)
            return runs.Count == 0
                ? ApiArray<CoachDay>.Empty
                : ApiArray.New(Build(day, runs, minVocabularyWords));

        // Runs are not language-bound: the neutral row carries them alone, so a filter by any language
        // (which always includes the neutral row) sees the day's turn-taking even without its own entries
        var rows = byLanguage
            .OrderBy(x => x.Key)
            .Select(x => Build(day, x.Value.Concat(runs), minVocabularyWords) with { Language = x.Key })
            .ToList();
        if (runs.Count > 0 && !byLanguage.ContainsKey(""))
            rows.Insert(0, Build(day, runs, minVocabularyWords) with { Language = "" });
        return rows.ToApiArray();
    }

    public static CoachDay Build(Moment day, IEnumerable<CoachRecord> records, int minVocabularyWords)
    {
        var d = new CoachDay(day);
        var fillers = new Dictionary<string, int>();
        var weak = new Dictionary<string, int>();
        var pace = new List<SpeechPaceSummary>();
        foreach (var r in records.Where(r => r.Day == day)) {
            if (r.Entry is { } e) {
                if (e.Pace is { } measurement)
                    pace.Add(SpeechPaceSummary.FromMeasurement(measurement));
                var words = e.Words ?? 0;
                var countsForVocabulary = e.Words >= minVocabularyWords;
                d = d with {
                    Entries = d.Entries + 1,
                    TaggedEntries = d.TaggedEntries + (e.IsTagged ? 1 : 0),
                    TaggedWords = d.TaggedWords + (e.IsTagged ? words : 0),
                    Words = d.Words + words,
                    Sentences = d.Sentences + (e.Sentences ?? 0),
                    Questions = d.Questions + (e.Questions ?? 0),
                    Repetitions = d.Repetitions + (e.Repetitions ?? 0),
                    Pauses = d.Pauses + (e.Pauses ?? 0),
                    FilledPauses = d.FilledPauses + e.FilledPauses,
                    Fillers = d.Fillers + e.Fillers,
                    WeakWords = d.WeakWords + e.WeakWords,
                    Profanities = d.Profanities + e.Profanities,
                    DurationSeconds = d.DurationSeconds + e.DurationSeconds,
                    SpeechSeconds = d.SpeechSeconds + (e.SpeechSeconds ?? e.DurationSeconds),
                    PauseSeconds = d.PauseSeconds + (e.PauseSeconds ?? 0),
                    VocabularyWords = d.VocabularyWords + (countsForVocabulary ? words : 0),
                    VocabularyDistinct = d.VocabularyDistinct + (countsForVocabulary ? e.DistinctWords ?? 0 : 0),
                };
                foreach (var s in e.Spans) {
                    var map = s.Kind switch {
                        SpeechSpanKind.FilledPause or SpeechSpanKind.Filler => fillers,
                        SpeechSpanKind.Weak => weak,
                        _ => null,
                    };
                    if (map is not null)
                        map[s.Word] = map.GetValueOrDefault(s.Word) + 1;
                }
            }
            if (r.Run is { } run)
                d = d with {
                    Runs = d.Runs + 1,
                    OwnTurns = d.OwnTurns + run.OwnTurns,
                    TotalTurns = d.TotalTurns + run.TotalTurns,
                    Responses = d.Responses + run.Responses,
                    Interruptions = d.Interruptions + run.Interruptions,
                    OwnSpeechSeconds = d.OwnSpeechSeconds + run.OwnSpeechSeconds,
                    TotalSpeechSeconds = d.TotalSpeechSeconds + run.TotalSpeechSeconds,
                    FairShareSeconds = d.FairShareSeconds
                        + (run.Participants > 0 ? run.TotalSpeechSeconds / run.Participants : 0),
                    LongestMonologueSeconds = Math.Max(d.LongestMonologueSeconds, run.LongestMonologueSeconds),
                    ResponseGapSeconds = d.ResponseGapSeconds + run.ResponseGapSeconds * run.Responses,
                };
        }
        return d with {
            FillerCounts = new ApiMap<string, int>(fillers),
            WeakWordCounts = new ApiMap<string, int>(weak),
            Pace = SpeechPaceSummary.Merge(pace),
        };
    }

    public static CoachDay Merge(Moment day, IEnumerable<CoachDay> days)
    {
        var d = new CoachDay(day);
        var fillers = new Dictionary<string, int>();
        var weak = new Dictionary<string, int>();
        var seenRunDays = new HashSet<Moment>();
        var pace = new List<SpeechPaceSummary>();
        foreach (var x in days) {
            if (x.Pace is { } summary)
                pace.Add(summary);
            var addRuns = seenRunDays.Add(x.Day);
            d = d with {
                Entries = d.Entries + x.Entries,
                TaggedEntries = d.TaggedEntries + x.TaggedEntries,
                TaggedWords = d.TaggedWords + x.TaggedWords,
                Words = d.Words + x.Words,
                Sentences = d.Sentences + x.Sentences,
                Questions = d.Questions + x.Questions,
                Repetitions = d.Repetitions + x.Repetitions,
                Pauses = d.Pauses + x.Pauses,
                FilledPauses = d.FilledPauses + x.FilledPauses,
                Fillers = d.Fillers + x.Fillers,
                WeakWords = d.WeakWords + x.WeakWords,
                Profanities = d.Profanities + x.Profanities,
                Runs = d.Runs + (addRuns ? x.Runs : 0),
                OwnTurns = d.OwnTurns + (addRuns ? x.OwnTurns : 0),
                TotalTurns = d.TotalTurns + (addRuns ? x.TotalTurns : 0),
                Responses = d.Responses + (addRuns ? x.Responses : 0),
                Interruptions = d.Interruptions + (addRuns ? x.Interruptions : 0),
                DurationSeconds = d.DurationSeconds + x.DurationSeconds,
                SpeechSeconds = d.SpeechSeconds + x.SpeechSeconds,
                PauseSeconds = d.PauseSeconds + x.PauseSeconds,
                VocabularyWords = d.VocabularyWords + x.VocabularyWords,
                VocabularyDistinct = d.VocabularyDistinct + x.VocabularyDistinct,
                OwnSpeechSeconds = d.OwnSpeechSeconds + (addRuns ? x.OwnSpeechSeconds : 0),
                TotalSpeechSeconds = d.TotalSpeechSeconds + (addRuns ? x.TotalSpeechSeconds : 0),
                FairShareSeconds = d.FairShareSeconds + (addRuns ? x.FairShareSeconds : 0),
                LongestMonologueSeconds = Math.Max(d.LongestMonologueSeconds, x.LongestMonologueSeconds),
                ResponseGapSeconds = d.ResponseGapSeconds + (addRuns ? x.ResponseGapSeconds : 0),
            };
            foreach (var (w, c) in x.FillerCounts)
                fillers[w] = fillers.GetValueOrDefault(w) + c;
            foreach (var (w, c) in x.WeakWordCounts)
                weak[w] = weak.GetValueOrDefault(w) + c;
        }
        return d with {
            FillerCounts = new ApiMap<string, int>(fillers),
            WeakWordCounts = new ApiMap<string, int>(weak),
            Pace = SpeechPaceSummary.Merge(pace),
        };
    }
}
