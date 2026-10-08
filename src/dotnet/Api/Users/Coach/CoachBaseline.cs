namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record CoachBaseline
{
    [DataMember, Key(0)] public string Id { get; init; } = "";
    [DataMember, Key(1)] public string Language { get; init; } = "";
    [DataMember, Key(2)] public CoachMetricKind Kind { get; init; }
    [DataMember, Key(3)] public Range<Moment> SourceRange { get; init; }
    [DataMember, Key(4)] public Moment CapturedAt { get; init; }
    [DataMember, Key(5)] public double Value { get; init; }
    [DataMember, Key(6)] public long Numerator { get; init; }
    [DataMember, Key(7)] public long Denominator { get; init; }
    [DataMember, Key(8)] public CoachSkillHistory History { get; init; } = CoachSkillHistory.None;
    [DataMember, Key(9)] public int MeasurementVersion { get; init; } = 1;
    [DataMember, Key(10)] public ApiArray<CoachBaselineSource> Sources { get; init; } = [];
    [DataMember, Key(11)] public Moment? InvalidatedAt { get; init; }
    [DataMember, Key(12)] public double Slow { get; init; }
    [DataMember, Key(13)] public double Fast { get; init; }
    [DataMember, Key(14)] public double OmittedSeconds { get; init; }
}

[DataContract, MessagePackObject]
public sealed partial record CoachBaselineSource(
    [property: DataMember, Key(0)] string SourceId,
    [property: DataMember, Key(1)] long Version);

[DataContract, MessagePackObject]
public sealed partial record CoachBaselineComparison
{
    [DataMember, Key(0)] public CoachBaseline? Baseline { get; init; }
    [DataMember, Key(1)] public CoachSkillHistory History { get; init; } = CoachSkillHistory.None;
    [DataMember, Key(2)] public bool? IsBetter { get; init; }
    [DataMember, Key(3)] public double? Change { get; init; }
    [DataMember, Key(4)] public bool HasRangeChanged { get; init; }
    [DataMember, Key(5)] public double OmittedSeconds { get; init; }
}
