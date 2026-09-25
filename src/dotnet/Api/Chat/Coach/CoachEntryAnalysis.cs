using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Speech-coach analysis of one voice entry: code metrics plus, once tagged, the LLM spans.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record CoachEntryAnalysis(
    [property: DataMember(Order = 0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember, Key(3)] public UserId UserId { get; init; }
    [DataMember, Key(4)] public Moment BeginsAt { get; init; }
    [DataMember, Key(5)] public Language? Language { get; init; }
    [DataMember, Key(6)] public double DurationSeconds { get; init; }
    [DataMember, Key(7)] public double? SpeechSeconds { get; init; }
    [DataMember, Key(8)] public int? Words { get; init; }
    [DataMember, Key(9)] public int? Sentences { get; init; }
    [DataMember, Key(10)] public int? Questions { get; init; }
    [DataMember, Key(11)] public int? Repetitions { get; init; }
    [DataMember, Key(12)] public int? DistinctWords { get; init; }
    [DataMember, Key(13)] public int? Pauses { get; init; }
    [DataMember, Key(14)] public double? PauseSeconds { get; init; }
    [DataMember, Key(15)] public ApiArray<SpeechSpan> Spans { get; init; }
    [DataMember, Key(16)] public int FilledPauses { get; init; }
    [DataMember, Key(17)] public int Fillers { get; init; }
    [DataMember, Key(18)] public int WeakWords { get; init; }
    [DataMember, Key(19)] public int Profanities { get; init; }
    [DataMember, Key(20)] public CoachTagState TagState { get; init; }
    [DataMember, Key(21)] public int PromptVersion { get; init; }
    [DataMember, Key(22)] public Moment? TaggedAt { get; init; }
    [DataMember, Key(23)] public HashString ContentHash { get; init; }

    // This record relies on referential equality
    public bool Equals(CoachEntryAnalysis? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
