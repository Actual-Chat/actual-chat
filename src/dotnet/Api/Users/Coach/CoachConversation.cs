using ActualChat.Chat;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record CoachConversation(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long StartEntryLid,
    [property: DataMember, Key(2)] Moment StartedAt,
    [property: DataMember, Key(3)] Moment EndedAt,
    [property: DataMember, Key(4)] string Language,
    [property: DataMember, Key(5)] string? SecondaryLanguage,
    [property: DataMember, Key(6)] int Words,
    [property: DataMember, Key(7)] double SpeechSeconds,
    [property: DataMember, Key(8)] int Fillers,
    [property: DataMember, Key(9)] int WeakWords,
    [property: DataMember, Key(10)] double? Pace,
    [property: DataMember, Key(11)] double? TalkShare,
    [property: DataMember, Key(12)] double? LongestMonologueSeconds,
    [property: DataMember, Key(13)] ApiMap<string, int> FillerCounts,
    [property: DataMember, Key(14)] ApiMap<string, int> WeakWordCounts
)
{
    [DataMember, Key(15)] public CoachBand PaceBand { get; init; }
    [DataMember, Key(16)] public CoachBand TalkShareBand { get; init; }
    [DataMember, Key(17)] public CoachBand MonologueBand { get; init; }
    [DataMember, Key(18)] public int Participants { get; init; }
    [DataMember, Key(19)] public CoachBand FillerBand { get; init; }
}

// A conversation is a run of one user's entries in one chat with no gap longer than `gap` between
// consecutive entries; a run in several languages gives one conversation per language, each with its
// own numbers. Runs overlapping it lend talk share and the monologue. Derived, never stored.
public static class CoachConversationBuilder
{
    private static readonly TimeSpan RunTolerance = TimeSpan.FromHours(1);

    // isLogCut: the records are only the newest ones, so the oldest run may lack its first
    // entries and is left out (unless it is the only one)
    public static ApiArray<CoachConversation> Build(
        IEnumerable<CoachRecord> records, TimeSpan gap, bool isLogCut = false)
    {
        var list = records.ToList();
        var groups = new List<List<CoachConversation>>();
        foreach (var chatGroup in list.Where(r => r.Entry is not null).GroupBy(r => r.ChatId)) {
            var runs = list.Where(r => r.Run is not null && r.ChatId == chatGroup.Key).ToList();
            var group = new List<CoachRecord>();
            foreach (var record in chatGroup.OrderBy(r => r.OccurredAt).ThenBy(r => r.Entry!.EntryLid)) {
                if (group.Count > 0 && record.OccurredAt - group.Max(EndOf) > gap) {
                    groups.Add(Close(group, runs));
                    group = [];
                }
                group.Add(record);
            }
            if (group.Count > 0)
                groups.Add(Close(group, runs));
        }
        var ordered = groups.OrderByDescending(g => g.Max(c => c.StartedAt)).ToList();
        if (isLogCut && ordered.Count > 1)
            ordered.RemoveAt(ordered.Count - 1);
        return ordered
            .SelectMany(g => g)
            .OrderByDescending(c => c.StartedAt)
            .ToApiArray();
    }

    // Private methods

    private static Moment EndOf(CoachRecord r)
        => r.OccurredAt + TimeSpan.FromSeconds(r.Entry!.DurationSeconds);

    private static string IsoOf(CoachRecord r)
        => r.Entry!.Language is { } l ? ActualChat.Language.GetIsoCode(l) : "";

    private static List<CoachConversation> Close(List<CoachRecord> group, List<CoachRecord> runs)
    {
        var groupStart = group[0].OccurredAt;
        var groupEnd = group.Max(EndOf);
        var overlapping = runs
            .Where(r => r.OccurredAt >= groupStart - RunTolerance && r.OccurredAt <= groupEnd + RunTolerance)
            .Select(r => r.Run!)
            .ToList();
        return group
            .GroupBy(IsoOf)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => CloseLanguage(g.Key, g.ToList(), overlapping))
            .ToList();
    }

    private static CoachConversation CloseLanguage(string iso, List<CoachRecord> group, List<CoachRunRecord> overlapping)
    {
        var entries = group.Select(r => r.Entry!).ToList();
        var startedAt = group[0].OccurredAt;
        var endedAt = group.Max(EndOf);
        var words = entries.Sum(e => e.Words ?? 0);
        var speech = entries.Sum(e => e.SpeechSeconds ?? e.DurationSeconds);
        var own = overlapping.Sum(r => r.OwnSpeechSeconds);
        var total = overlapping.Sum(r => r.TotalSpeechSeconds);
        var fillers = new Dictionary<string, int>();
        var weak = new Dictionary<string, int>();
        foreach (var span in entries.SelectMany(e => e.Spans)) {
            var map = span.Kind switch {
                SpeechSpanKind.FilledPause or SpeechSpanKind.Filler => fillers,
                SpeechSpanKind.Weak => weak,
                _ => null,
            };
            if (map is not null)
                map[span.Word] = map.GetValueOrDefault(span.Word) + 1;
        }
        return new CoachConversation(
            group[0].ChatId,
            entries[0].EntryLid,
            startedAt,
            endedAt,
            iso,
            null,
            words,
            speech,
            entries.Sum(e => e.FilledPauses + e.Fillers),
            entries.Sum(e => e.WeakWords),
            speech > 0 && words > 0 ? words * 60 / speech : null,
            total > 0 ? own / total : null,
            overlapping.Count > 0 ? overlapping.Max(r => r.LongestMonologueSeconds) : null,
            new ApiMap<string, int>(fillers),
            new ApiMap<string, int>(weak)) {
            Participants = overlapping.Count > 0 ? overlapping.Max(r => r.Participants) : 0,
        };
    }
}
