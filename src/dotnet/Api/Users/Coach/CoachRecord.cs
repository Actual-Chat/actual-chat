using ActualChat.Chat;

namespace ActualChat.Users;

public enum CoachRecordKind
{
    Entry = 0,
    Run = 1,
}

[DataContract, MessagePackObject]
public sealed partial record CoachEntryRecord(
    [property: DataMember, Key(0)] long EntryLid,
    [property: DataMember, Key(1)] string? Language,
    [property: DataMember, Key(2)] double DurationSeconds,
    [property: DataMember, Key(3)] double? SpeechSeconds,
    [property: DataMember, Key(4)] int? Words,
    [property: DataMember, Key(5)] int? Sentences,
    [property: DataMember, Key(6)] int? Questions,
    [property: DataMember, Key(7)] int? Repetitions,
    [property: DataMember, Key(8)] int? DistinctWords,
    [property: DataMember, Key(9)] int? Pauses,
    [property: DataMember, Key(10)] double? PauseSeconds,
    [property: DataMember, Key(11)] bool IsTagged,
    [property: DataMember, Key(12)] int FilledPauses,
    [property: DataMember, Key(13)] int Fillers,
    [property: DataMember, Key(14)] int WeakWords,
    [property: DataMember, Key(15)] int Profanities,
    [property: DataMember, Key(16)] ApiArray<SpeechSpan> Spans
)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [DataMember, Key(17)] public SpeechPaceMeasurement? Pace { get; init; }
}

[DataContract, MessagePackObject]
public sealed partial record CoachRunRecord(
    [property: DataMember, Key(0)] long StartEntryLid,
    [property: DataMember, Key(1)] double OwnSpeechSeconds,
    [property: DataMember, Key(2)] double TotalSpeechSeconds,
    [property: DataMember, Key(3)] int OwnTurns,
    [property: DataMember, Key(4)] int TotalTurns,
    [property: DataMember, Key(5)] int Participants,
    [property: DataMember, Key(6)] double LongestMonologueSeconds,
    [property: DataMember, Key(7)] int Responses,
    [property: DataMember, Key(8)] double ResponseGapSeconds,
    [property: DataMember, Key(9)] int Interruptions
);

/// <summary>
/// One row of a user's speech-coach log: a voice entry's analysis or a run's turn-taking,
/// as the chat side emitted it. Day rows are rebuilt from these.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CoachRecord(
    [property: DataMember, Key(0)] CoachRecordKind Kind,
    [property: DataMember, Key(1)] string SourceId,
    [property: DataMember, Key(2)] UserId UserId,
    [property: DataMember, Key(3)] ChatId ChatId,
    [property: DataMember, Key(4)] Moment OccurredAt)
{
    [DataMember, Key(5)] public CoachEntryRecord? Entry { get; init; }
    [DataMember, Key(6)] public CoachRunRecord? Run { get; init; }
    // The chat-side analysis version: a lower one is a late or duplicate delivery and is ignored
    [DataMember, Key(7)] public long Version { get; init; }
    // Set by the user: the row stays in the log but counts in no score, progress or tip
    [DataMember, Key(8)] public bool IsExcluded { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment Day => UsageDay.DayOf(OccurredAt);

    public static CoachRecord FromEntry(CoachEntryAnalysis a)
        => new (CoachRecordKind.Entry, a.Id.Value, a.UserId, a.Id.ChatId, a.BeginsAt) {
            Version = a.Version,
            Entry = new CoachEntryRecord(
                a.Id.LocalId,
                a.Language?.Value,
                a.DurationSeconds,
                a.SpeechSeconds,
                a.Words,
                a.Sentences,
                a.Questions,
                a.Repetitions,
                a.DistinctWords,
                a.Pauses,
                a.PauseSeconds,
                a.TagState == CoachTagState.Tagged,
                a.FilledPauses,
                a.Fillers,
                a.WeakWords,
                a.Profanities,
                a.Spans.Select(s => s with { Synonyms = ApiArray<string>.Empty }).ToApiArray()) {
                Pace = a.Pace,
            },
        };

    public static CoachRecord FromRun(CoachConversationAnalysis a)
        => new (CoachRecordKind.Run, $"{a.Id}:{a.AuthorId}", a.UserId, a.Id.ChatId, a.EndsAt) {
            Version = a.Version,
            Run = new CoachRunRecord(
                a.Id.StartEntryLid,
                a.OwnSpeechSeconds,
                a.TotalSpeechSeconds,
                a.OwnTurns,
                a.TotalTurns,
                a.Participants,
                a.LongestMonologueSeconds,
                a.Responses,
                a.ResponseGapSeconds,
                a.Interruptions),
        };
}
