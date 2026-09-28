namespace ActualChat.Users;

public enum CoachWindow
{
    Today = 0,
    Week = 1,
    Month = 2,
    AllTime = 3,
}

public enum CoachMetricKind
{
    Pace = 0,
    Pauses = 1,
    Fillers = 2,
    WeakWords = 3,
    Repetition = 4,
    Profanity = 5,
    Questions = 6,
    SentenceLength = 7,
    Vocabulary = 8,
    TurnTaking = 9,
    Patience = 10,
    Interruptions = 11,
    Monologue = 12,
}

// High doubles as "fast" and "long", Low as "slow" and "short"; the UI labels per kind
public enum CoachBand
{
    None = 0,
    Good = 1,
    Medium = 2,
    High = 3,
    Low = 4,
}

[DataContract, MessagePackObject]
public sealed partial record CoachChip(
    [property: DataMember, Key(0)] string Word,
    [property: DataMember, Key(1)] int Count
);

[DataContract, MessagePackObject]
public sealed partial record CoachMetric(
    [property: DataMember, Key(0)] CoachMetricKind Kind,
    [property: DataMember, Key(1)] double? Value,
    [property: DataMember, Key(2)] double? Rate,
    [property: DataMember, Key(3)] CoachBand Band,
    [property: DataMember, Key(4)] ApiArray<CoachChip> Chips
);

[DataContract, MessagePackObject]
public sealed partial record CoachSummary(
    [property: DataMember, Key(0)] CoachWindow Window,
    [property: DataMember, Key(1)] int? Score,
    [property: DataMember, Key(2)] int? ScoreDelta,
    [property: DataMember, Key(3)] int Words,
    [property: DataMember, Key(4)] int Entries,
    [property: DataMember, Key(5)] int TaggedEntries,
    [property: DataMember, Key(6)] ApiArray<CoachMetric> Metrics)
{
    public static readonly CoachSummary None
        = new (CoachWindow.Today, null, null, 0, 0, 0, ApiArray<CoachMetric>.Empty);
}

[DataContract, MessagePackObject]
public sealed partial record CoachOccurrence(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long EntryLid,
    [property: DataMember, Key(2)] int Start,
    [property: DataMember, Key(3)] int Length,
    [property: DataMember, Key(4)] Moment At
);
