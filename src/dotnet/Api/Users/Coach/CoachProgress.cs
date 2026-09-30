namespace ActualChat.Users;

public enum CoachMilestoneKind
{
    Words1K = 0,
    Words10K = 1,
    Words100K = 2,
    FiveDayWeek = 3,
    CleanFillerWeek = 4,
    NoLongMonologueWeek = 5,
    RisingMonth = 6,
}

[DataContract, MessagePackObject]
public sealed partial record CoachScorePart(
    [property: DataMember, Key(0)] CoachMetricKind Kind,
    [property: DataMember, Key(1)] int Weight,
    [property: DataMember, Key(2)] CoachBand Band,
    [property: DataMember, Key(3)] double Points,
    [property: DataMember, Key(4)] double MaxPoints
);

// Previous and Current are the metric's Rate when it has one, else its Value, so callers format
// them with CoachLabels the same way as a metric
[DataContract, MessagePackObject]
public sealed partial record CoachWeekDelta(
    [property: DataMember, Key(0)] CoachMetricKind Kind,
    [property: DataMember, Key(1)] double? Previous,
    [property: DataMember, Key(2)] double? Current,
    [property: DataMember, Key(3)] CoachBand Band,
    [property: DataMember, Key(4)] bool? IsBetter
);

[DataContract, MessagePackObject]
public sealed partial record CoachMilestone(
    [property: DataMember, Key(0)] CoachMilestoneKind Kind,
    [property: DataMember, Key(1)] Moment? AchievedAt
);

[DataContract, MessagePackObject]
public sealed partial record CoachLanguageInfo(
    [property: DataMember, Key(0)] string Iso,
    [property: DataMember, Key(1)] CoachLanguageLevel Level,
    [property: DataMember, Key(2)] int Words30Days,
    [property: DataMember, Key(3)] bool IsWordSplittable
);

[DataContract, MessagePackObject]
public sealed partial record CoachWeekScore(
    [property: DataMember, Key(0)] Moment WeekStart,
    [property: DataMember, Key(1)] int? Score
);
